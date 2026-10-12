using System.Collections.Concurrent;
using System.Globalization;
using Microsoft.Data.Sqlite;

namespace FEBuddyDiscordBot.DataAccess;

/// <summary>
/// Which cycles' d-TPP Metafiles have been announced, and which members turned the announcement pings off.
/// </summary>
public sealed class AiracStore
{
    private readonly BotDatabase _database;

    /// <summary>Members who turned the pings off. Read for every role check, so kept in memory.</summary>
    private readonly ConcurrentDictionary<ulong, byte> _optedOut = new();

    public AiracStore(BotDatabase database)
    {
        _database = database;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _database.OpenAsync(cancellationToken);

        await using (SqliteCommand create = connection.CreateCommand())
        {
            create.CommandText = """
                CREATE TABLE IF NOT EXISTS airac_metafile_announcements (
                    cycle_id      TEXT PRIMARY KEY,
                    found_utc     TEXT NOT NULL,
                    channel_id    INTEGER NOT NULL,
                    message_id    INTEGER NOT NULL
                );
                CREATE TABLE IF NOT EXISTS airac_ping_opt_outs (
                    user_id       INTEGER PRIMARY KEY,
                    opted_out_utc TEXT NOT NULL
                );
                """;
            await create.ExecuteNonQueryAsync(cancellationToken);
        }

        await using SqliteCommand select = connection.CreateCommand();
        select.CommandText = "SELECT user_id FROM airac_ping_opt_outs";
        await using SqliteDataReader reader = await select.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            _optedOut[(ulong)reader.GetInt64(0)] = 0;
        }
    }

    public bool IsOptedOut(ulong userId) => _optedOut.ContainsKey(userId);

    /// <summary>Turn a member's pings off (<paramref name="optedOut"/> true) or back on.</summary>
    public async Task SetOptedOutAsync(ulong userId, bool optedOut, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _database.OpenAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = optedOut
            ? "INSERT INTO airac_ping_opt_outs (user_id, opted_out_utc) VALUES ($userId, $now) ON CONFLICT(user_id) DO NOTHING"
            : "DELETE FROM airac_ping_opt_outs WHERE user_id = $userId";
        command.Parameters.AddWithValue("$userId", (long)userId);
        command.Parameters.AddWithValue("$now", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        await command.ExecuteNonQueryAsync(cancellationToken);

        if (optedOut) _optedOut[userId] = 0;
        else _optedOut.TryRemove(userId, out _);
    }

    public async Task<bool> WasAnnouncedAsync(string cycleId, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _database.OpenAsync(cancellationToken);
        await using SqliteCommand select = connection.CreateCommand();
        select.CommandText = "SELECT 1 FROM airac_metafile_announcements WHERE cycle_id = $cycleId";
        select.Parameters.AddWithValue("$cycleId", cycleId);
        return await select.ExecuteScalarAsync(cancellationToken) is not null;
    }

    public async Task RecordAnnouncementAsync(string cycleId, DateTimeOffset foundUtc, ulong channelId, ulong messageId, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _database.OpenAsync(cancellationToken);
        await using SqliteCommand insert = connection.CreateCommand();
        insert.CommandText = """
            INSERT INTO airac_metafile_announcements (cycle_id, found_utc, channel_id, message_id) VALUES ($cycleId, $found, $channelId, $messageId)
            ON CONFLICT(cycle_id) DO NOTHING;
            """;
        insert.Parameters.AddWithValue("$cycleId", cycleId);
        insert.Parameters.AddWithValue("$found", foundUtc.ToString("O", CultureInfo.InvariantCulture));
        insert.Parameters.AddWithValue("$channelId", (long)channelId);
        insert.Parameters.AddWithValue("$messageId", (long)messageId);
        await insert.ExecuteNonQueryAsync(cancellationToken);
    }
}
