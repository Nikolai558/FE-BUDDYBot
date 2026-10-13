using System.Globalization;
using FEBuddyDiscordBot.Announcements;
using Microsoft.Data.Sqlite;

namespace FEBuddyDiscordBot.DataAccess;

public enum ReleaseStatus
{
    /// <summary>Seen but not announced: it was out before the bot started watching, below the chosen level, or announcements were off.</summary>
    Skipped,

    /// <summary>A preview is waiting in the review channel for an admin to post, edit or cancel it.</summary>
    Pending,

    /// <summary>An admin pressed Post and the bot is posting it.</summary>
    Posting,

    Posted,
    Cancelled,
}

public sealed record ReleaseRecord(
    string Tag,
    ReleaseLevel Level,
    ReleaseStatus Status,
    string? Url,
    ulong? ReviewChannelId,
    ulong? ReviewMessageId,
    ulong? PublicChannelId,
    ulong? PublicMessageId);

/// <summary>
/// Every FE-BUDDY release the bot has seen, and what happened to its announcement. Each release is handled once.
/// </summary>
public sealed class ReleaseStore
{
    /// <summary>
    /// A row saying the bot has started watching. The first check records the releases already out as skipped, so they're
    /// never announced; this marker tells later checks that already happened, even if there were no releases then.
    /// </summary>
    public const string StartedMarker = "*";

    private readonly BotDatabase _database;

    public ReleaseStore(BotDatabase database)
    {
        _database = database;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _database.OpenAsync(cancellationToken);
        await using SqliteCommand create = connection.CreateCommand();
        create.CommandText = """
            CREATE TABLE IF NOT EXISTS release_announcements (
                tag               TEXT PRIMARY KEY,
                level             TEXT NOT NULL,
                status            TEXT NOT NULL,
                url               TEXT,
                seen_utc          TEXT NOT NULL,
                review_channel_id INTEGER,
                review_message_id INTEGER,
                public_channel_id INTEGER,
                public_message_id INTEGER,
                resolved_by       INTEGER,
                resolved_utc      TEXT
            );
            """;
        await create.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>Tags of every release seen so far, plus <see cref="StartedMarker"/> once the bot has started watching.</summary>
    public async Task<HashSet<string>> GetSeenTagsAsync(CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _database.OpenAsync(cancellationToken);
        await using SqliteCommand select = connection.CreateCommand();
        select.CommandText = "SELECT tag FROM release_announcements";
        await using SqliteDataReader reader = await select.ExecuteReaderAsync(cancellationToken);

        HashSet<string> tags = new(StringComparer.Ordinal);
        while (await reader.ReadAsync(cancellationToken)) tags.Add(reader.GetString(0));
        return tags;
    }

    /// <summary>When the bot started watching (the <see cref="StartedMarker"/> row), or null if it hasn't yet.</summary>
    public async Task<DateTimeOffset?> GetWatchStartedAsync(CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _database.OpenAsync(cancellationToken);
        await using SqliteCommand select = connection.CreateCommand();
        select.CommandText = "SELECT seen_utc FROM release_announcements WHERE tag = $tag";
        select.Parameters.AddWithValue("$tag", StartedMarker);
        return await select.ExecuteScalarAsync(cancellationToken) is string seen
            ? DateTimeOffset.Parse(seen, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)
            : null;
    }

    public async Task RecordSkippedAsync(string tag, ReleaseLevel level, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _database.OpenAsync(cancellationToken);
        await using SqliteCommand insert = connection.CreateCommand();
        insert.CommandText = """
            INSERT INTO release_announcements (tag, level, status, seen_utc) VALUES ($tag, $level, $status, $now)
            ON CONFLICT(tag) DO NOTHING;
            """;
        insert.Parameters.AddWithValue("$tag", tag);
        insert.Parameters.AddWithValue("$level", level.ToString());
        insert.Parameters.AddWithValue("$status", nameof(ReleaseStatus.Skipped));
        insert.Parameters.AddWithValue("$now", Now());
        await insert.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task RecordPendingAsync(string tag, ReleaseLevel level, string url, ulong reviewChannelId, ulong reviewMessageId, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _database.OpenAsync(cancellationToken);
        await using SqliteCommand insert = connection.CreateCommand();
        insert.CommandText = """
            INSERT INTO release_announcements (tag, level, status, url, seen_utc, review_channel_id, review_message_id)
            VALUES ($tag, $level, $status, $url, $now, $channel, $message)
            ON CONFLICT(tag) DO NOTHING;
            """;
        insert.Parameters.AddWithValue("$tag", tag);
        insert.Parameters.AddWithValue("$level", level.ToString());
        insert.Parameters.AddWithValue("$status", nameof(ReleaseStatus.Pending));
        insert.Parameters.AddWithValue("$url", url);
        insert.Parameters.AddWithValue("$now", Now());
        insert.Parameters.AddWithValue("$channel", (long)reviewChannelId);
        insert.Parameters.AddWithValue("$message", (long)reviewMessageId);
        await insert.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<ReleaseRecord?> GetAsync(string tag, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _database.OpenAsync(cancellationToken);
        await using SqliteCommand select = connection.CreateCommand();
        select.CommandText = """
            SELECT level, status, url, review_channel_id, review_message_id, public_channel_id, public_message_id
            FROM release_announcements WHERE tag = $tag
            """;
        select.Parameters.AddWithValue("$tag", tag);
        await using SqliteDataReader reader = await select.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;

        ulong? Id(int column) => reader.IsDBNull(column) ? null : (ulong)reader.GetInt64(column);
        return new(tag, Enum.Parse<ReleaseLevel>(reader.GetString(0)), Enum.Parse<ReleaseStatus>(reader.GetString(1)),
            reader.IsDBNull(2) ? null : reader.GetString(2), Id(3), Id(4), Id(5), Id(6));
    }

    /// <summary>
    /// Move a release from <paramref name="from"/> to <paramref name="to"/>. Returns false if it isn't in <paramref name="from"/>
    /// (another admin got there first), so only one click acts.
    /// </summary>
    public async Task<bool> TryMoveAsync(string tag, ReleaseStatus from, ReleaseStatus to, ulong? byUserId = null, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _database.OpenAsync(cancellationToken);
        await using SqliteCommand update = connection.CreateCommand();
        update.CommandText = """
            UPDATE release_announcements SET status = $to, resolved_by = COALESCE($by, resolved_by), resolved_utc = $now
            WHERE tag = $tag AND status = $from
            """;
        update.Parameters.AddWithValue("$tag", tag);
        update.Parameters.AddWithValue("$from", from.ToString());
        update.Parameters.AddWithValue("$to", to.ToString());
        update.Parameters.AddWithValue("$by", byUserId is ulong by ? (long)by : DBNull.Value);
        update.Parameters.AddWithValue("$now", Now());
        return await update.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    public async Task MarkPostedAsync(string tag, ulong channelId, ulong messageId, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _database.OpenAsync(cancellationToken);
        await using SqliteCommand update = connection.CreateCommand();
        update.CommandText = """
            UPDATE release_announcements SET status = $status, public_channel_id = $channel, public_message_id = $message
            WHERE tag = $tag
            """;
        update.Parameters.AddWithValue("$tag", tag);
        update.Parameters.AddWithValue("$status", nameof(ReleaseStatus.Posted));
        update.Parameters.AddWithValue("$channel", (long)channelId);
        update.Parameters.AddWithValue("$message", (long)messageId);
        await update.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string Now() => DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
}
