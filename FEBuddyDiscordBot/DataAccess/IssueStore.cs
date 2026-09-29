using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace FEBuddyDiscordBot.DataAccess;

public enum SubmissionStatus
{
    /// <summary>Waiting for an approver.</summary>
    Pending,

    /// <summary>The GitHub issue was created.</summary>
    Created,

    Denied,
}

public sealed record Submission(long Id, ulong UserId, SubmissionStatus Status, string DraftJson, ulong? ApprovalMessageId);

/// <summary>What a forum post shows: its title, whether it's open, its tags, and a hash of the issue text.</summary>
public sealed record PostState(string Title, bool IsOpen, string[] Tags, string BodyHash)
{
    public bool Matches(PostState other) =>
        Title == other.Title && IsOpen == other.IsOpen && BodyHash == other.BodyHash && Tags.SequenceEqual(other.Tags);
}

/// <summary>
/// Which forum post belongs to which GitHub issue, and each member's submissions (for the hourly limit and approvals).
/// </summary>
public sealed class IssueStore
{
    private readonly BotDatabase _database;

    /// <summary>Issue number → its forum post (thread) and the forum it's in. Small, so kept in memory.</summary>
    private readonly ConcurrentDictionary<int, (ulong ThreadId, ulong ForumId)> _posts = new();

    public IssueStore(BotDatabase database)
    {
        _database = database;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _database.OpenAsync(cancellationToken);

        await using (SqliteCommand create = connection.CreateCommand())
        {
            create.CommandText = """
                CREATE TABLE IF NOT EXISTS issue_posts (
                    issue_number INTEGER PRIMARY KEY,
                    thread_id    INTEGER NOT NULL,
                    forum_id     INTEGER NOT NULL,
                    reporter_id  INTEGER,
                    created_utc  TEXT NOT NULL
                );
                CREATE TABLE IF NOT EXISTS issue_submissions (
                    id                  INTEGER PRIMARY KEY AUTOINCREMENT,
                    user_id             INTEGER NOT NULL,
                    status              TEXT NOT NULL,
                    draft_json          TEXT NOT NULL,
                    approval_message_id INTEGER,
                    issue_number        INTEGER,
                    created_utc         TEXT NOT NULL
                );
                CREATE INDEX IF NOT EXISTS ix_issue_submissions_user ON issue_submissions (user_id, created_utc);
                CREATE TABLE IF NOT EXISTS issue_post_state (
                    issue_number INTEGER PRIMARY KEY,
                    json         TEXT NOT NULL
                );
                CREATE TABLE IF NOT EXISTS issue_comments (
                    comment_id   INTEGER PRIMARY KEY,
                    issue_number INTEGER NOT NULL,
                    message_id   INTEGER NOT NULL,
                    updated_utc  TEXT NOT NULL
                );
                CREATE TABLE IF NOT EXISTS sync_state (
                    key   TEXT PRIMARY KEY,
                    value TEXT NOT NULL
                );
                CREATE TABLE IF NOT EXISTS discord_replies (
                    message_id   INTEGER PRIMARY KEY,
                    issue_number INTEGER NOT NULL,
                    comment_id   INTEGER NOT NULL
                );
                CREATE TABLE IF NOT EXISTS github_links (
                    user_id    INTEGER PRIMARY KEY,
                    login      TEXT NOT NULL,
                    linked_utc TEXT NOT NULL
                );
                """;
            await create.ExecuteNonQueryAsync(cancellationToken);
        }

        await using SqliteCommand select = connection.CreateCommand();
        select.CommandText = "SELECT issue_number, thread_id, forum_id FROM issue_posts";
        await using SqliteDataReader reader = await select.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            _posts[reader.GetInt32(0)] = ((ulong)reader.GetInt64(1), (ulong)reader.GetInt64(2));
        }
    }

    /// <summary>The issue's forum post, or null if it has none.</summary>
    public ulong? GetPostId(int issueNumber) => _posts.TryGetValue(issueNumber, out var post) ? post.ThreadId : null;

    /// <summary>True if the issue has a post in this forum (after switching forums, old posts don't count).</summary>
    public bool HasPostIn(int issueNumber, ulong forumId) => _posts.TryGetValue(issueNumber, out var post) && post.ForumId == forumId;

    public async Task SavePostAsync(int issueNumber, ulong threadId, ulong forumId, ulong? reporterId, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _database.OpenAsync(cancellationToken);
        await using SqliteCommand insert = connection.CreateCommand();
        insert.CommandText = """
            INSERT INTO issue_posts (issue_number, thread_id, forum_id, reporter_id, created_utc) VALUES ($issue, $thread, $forum, $reporter, $now)
            ON CONFLICT(issue_number) DO UPDATE SET thread_id = excluded.thread_id, forum_id = excluded.forum_id;
            """;
        insert.Parameters.AddWithValue("$issue", issueNumber);
        insert.Parameters.AddWithValue("$thread", (long)threadId);
        insert.Parameters.AddWithValue("$forum", (long)forumId);
        insert.Parameters.AddWithValue("$reporter", reporterId is ulong id ? (long)id : DBNull.Value);
        insert.Parameters.AddWithValue("$now", Now());
        await insert.ExecuteNonQueryAsync(cancellationToken);

        _posts[issueNumber] = (threadId, forumId);
    }

    /// <summary>Submissions by this member since the given time that were created or are still waiting.</summary>
    public async Task<int> CountSubmissionsSinceAsync(ulong userId, DateTimeOffset since, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _database.OpenAsync(cancellationToken);
        await using SqliteCommand count = connection.CreateCommand();
        count.CommandText = "SELECT COUNT(*) FROM issue_submissions WHERE user_id = $user AND created_utc >= $since AND status <> 'Denied'";
        count.Parameters.AddWithValue("$user", (long)userId);
        count.Parameters.AddWithValue("$since", since.UtcDateTime.ToString("O", CultureInfo.InvariantCulture));
        return Convert.ToInt32(await count.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
    }

    public async Task<long> AddSubmissionAsync(ulong userId, SubmissionStatus status, string draftJson, int? issueNumber, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _database.OpenAsync(cancellationToken);
        await using SqliteCommand insert = connection.CreateCommand();
        insert.CommandText = """
            INSERT INTO issue_submissions (user_id, status, draft_json, issue_number, created_utc) VALUES ($user, $status, $draft, $issue, $now);
            SELECT last_insert_rowid();
            """;
        insert.Parameters.AddWithValue("$user", (long)userId);
        insert.Parameters.AddWithValue("$status", status.ToString());
        insert.Parameters.AddWithValue("$draft", draftJson);
        insert.Parameters.AddWithValue("$issue", issueNumber is int n ? n : DBNull.Value);
        insert.Parameters.AddWithValue("$now", Now());
        return Convert.ToInt64(await insert.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture);
    }

    public async Task SetApprovalMessageAsync(long submissionId, ulong messageId, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _database.OpenAsync(cancellationToken);
        await using SqliteCommand update = connection.CreateCommand();
        update.CommandText = "UPDATE issue_submissions SET approval_message_id = $message WHERE id = $id";
        update.Parameters.AddWithValue("$message", (long)messageId);
        update.Parameters.AddWithValue("$id", submissionId);
        await update.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<Submission?> GetSubmissionAsync(long submissionId, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _database.OpenAsync(cancellationToken);
        await using SqliteCommand select = connection.CreateCommand();
        select.CommandText = "SELECT id, user_id, status, draft_json, approval_message_id FROM issue_submissions WHERE id = $id";
        select.Parameters.AddWithValue("$id", submissionId);
        await using SqliteDataReader reader = await select.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;

        return new Submission(
            reader.GetInt64(0),
            (ulong)reader.GetInt64(1),
            Enum.Parse<SubmissionStatus>(reader.GetString(2)),
            reader.GetString(3),
            reader.IsDBNull(4) ? null : (ulong)reader.GetInt64(4));
    }

    /// <summary>
    /// Move a pending submission to its final status. Returns false if it was no longer pending
    /// (e.g. two approvers pressed a button at the same time), so only one of them acts on it.
    /// </summary>
    public async Task<bool> TryResolveAsync(long submissionId, SubmissionStatus status, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _database.OpenAsync(cancellationToken);
        await using SqliteCommand update = connection.CreateCommand();
        update.CommandText = "UPDATE issue_submissions SET status = $status WHERE id = $id AND status = 'Pending'";
        update.Parameters.AddWithValue("$status", status.ToString());
        update.Parameters.AddWithValue("$id", submissionId);
        return await update.ExecuteNonQueryAsync(cancellationToken) == 1;
    }

    /// <summary>Put a submission back to pending, e.g. when creating its issue failed after it was approved.</summary>
    public async Task ReopenAsync(long submissionId, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _database.OpenAsync(cancellationToken);
        await using SqliteCommand update = connection.CreateCommand();
        update.CommandText = "UPDATE issue_submissions SET status = 'Pending' WHERE id = $id";
        update.Parameters.AddWithValue("$id", submissionId);
        await update.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SetIssueNumberAsync(long submissionId, int issueNumber, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _database.OpenAsync(cancellationToken);
        await using SqliteCommand update = connection.CreateCommand();
        update.CommandText = "UPDATE issue_submissions SET issue_number = $issue WHERE id = $id";
        update.Parameters.AddWithValue("$issue", issueNumber);
        update.Parameters.AddWithValue("$id", submissionId);
        await update.ExecuteNonQueryAsync(cancellationToken);
    }

    // ---- GitHub → Discord sync ----

    /// <summary>What a post last showed, so the sync only touches posts whose issue actually changed.</summary>
    public async Task<PostState?> GetPostStateAsync(int issueNumber, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _database.OpenAsync(cancellationToken);
        await using SqliteCommand select = connection.CreateCommand();
        select.CommandText = "SELECT json FROM issue_post_state WHERE issue_number = $issue";
        select.Parameters.AddWithValue("$issue", issueNumber);
        return await select.ExecuteScalarAsync(cancellationToken) is string json ? JsonSerializer.Deserialize<PostState>(json) : null;
    }

    public async Task SavePostStateAsync(int issueNumber, PostState state, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _database.OpenAsync(cancellationToken);
        await using SqliteCommand upsert = connection.CreateCommand();
        upsert.CommandText = """
            INSERT INTO issue_post_state (issue_number, json) VALUES ($issue, $json)
            ON CONFLICT(issue_number) DO UPDATE SET json = excluded.json;
            """;
        upsert.Parameters.AddWithValue("$issue", issueNumber);
        upsert.Parameters.AddWithValue("$json", JsonSerializer.Serialize(state));
        await upsert.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>Forget an issue's post (e.g. someone deleted it in Discord). Its comments are forgotten too.</summary>
    public async Task ForgetPostAsync(int issueNumber, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _database.OpenAsync(cancellationToken);
        await using SqliteCommand delete = connection.CreateCommand();
        delete.CommandText = """
            DELETE FROM issue_posts WHERE issue_number = $issue;
            DELETE FROM issue_post_state WHERE issue_number = $issue;
            DELETE FROM issue_comments WHERE issue_number = $issue;
            """;
        delete.Parameters.AddWithValue("$issue", issueNumber);
        await delete.ExecuteNonQueryAsync(cancellationToken);

        _posts.TryRemove(issueNumber, out _);
    }

    /// <summary>The Discord message a GitHub comment was copied to, and the comment's last edit time then.</summary>
    public async Task<(ulong MessageId, DateTimeOffset UpdatedUtc)?> GetCommentAsync(long commentId, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _database.OpenAsync(cancellationToken);
        await using SqliteCommand select = connection.CreateCommand();
        select.CommandText = "SELECT message_id, updated_utc FROM issue_comments WHERE comment_id = $comment";
        select.Parameters.AddWithValue("$comment", commentId);
        await using SqliteDataReader reader = await select.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return ((ulong)reader.GetInt64(0), DateTimeOffset.Parse(reader.GetString(1), CultureInfo.InvariantCulture));
    }

    public async Task SaveCommentAsync(long commentId, int issueNumber, ulong messageId, DateTimeOffset updatedUtc, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _database.OpenAsync(cancellationToken);
        await using SqliteCommand upsert = connection.CreateCommand();
        upsert.CommandText = """
            INSERT INTO issue_comments (comment_id, issue_number, message_id, updated_utc) VALUES ($comment, $issue, $message, $updated)
            ON CONFLICT(comment_id) DO UPDATE SET message_id = excluded.message_id, updated_utc = excluded.updated_utc;
            """;
        upsert.Parameters.AddWithValue("$comment", commentId);
        upsert.Parameters.AddWithValue("$issue", issueNumber);
        upsert.Parameters.AddWithValue("$message", (long)messageId);
        upsert.Parameters.AddWithValue("$updated", updatedUtc.ToString("O", CultureInfo.InvariantCulture));
        await upsert.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>Where the GitHub sync got to. Null before the first sync.</summary>
    public async Task<DateTimeOffset?> GetSyncCursorAsync(CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _database.OpenAsync(cancellationToken);
        await using SqliteCommand select = connection.CreateCommand();
        select.CommandText = "SELECT value FROM sync_state WHERE key = 'github_cursor'";
        return await select.ExecuteScalarAsync(cancellationToken) is string value ? DateTimeOffset.Parse(value, CultureInfo.InvariantCulture) : null;
    }

    public async Task SetSyncCursorAsync(DateTimeOffset cursor, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _database.OpenAsync(cancellationToken);
        await using SqliteCommand upsert = connection.CreateCommand();
        upsert.CommandText = """
            INSERT INTO sync_state (key, value) VALUES ('github_cursor', $value)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value;
            """;
        upsert.Parameters.AddWithValue("$value", cursor.ToString("O", CultureInfo.InvariantCulture));
        await upsert.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>When the oldest forum post was made, or null if there are none.</summary>
    public async Task<DateTimeOffset?> GetOldestPostTimeAsync(CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _database.OpenAsync(cancellationToken);
        await using SqliteCommand select = connection.CreateCommand();
        select.CommandText = "SELECT MIN(created_utc) FROM issue_posts";
        return await select.ExecuteScalarAsync(cancellationToken) is string value ? DateTimeOffset.Parse(value, CultureInfo.InvariantCulture) : null;
    }

    // ---- Discord → GitHub ----

    /// <summary>The issue whose forum post this thread is, or null if it isn't one.</summary>
    public int? GetIssueForThread(ulong threadId) =>
        _posts.FirstOrDefault(p => p.Value.ThreadId == threadId) is { Value.ThreadId: not 0 } post ? post.Key : null;

    /// <summary>The GitHub comment a Discord message was copied to, or null if it wasn't.</summary>
    public async Task<(int IssueNumber, long CommentId)?> GetReplyAsync(ulong messageId, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _database.OpenAsync(cancellationToken);
        await using SqliteCommand select = connection.CreateCommand();
        select.CommandText = "SELECT issue_number, comment_id FROM discord_replies WHERE message_id = $message";
        select.Parameters.AddWithValue("$message", (long)messageId);
        await using SqliteDataReader reader = await select.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? (reader.GetInt32(0), reader.GetInt64(1)) : null;
    }

    public async Task SaveReplyAsync(ulong messageId, int issueNumber, long commentId, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _database.OpenAsync(cancellationToken);
        await using SqliteCommand insert = connection.CreateCommand();
        insert.CommandText = "INSERT OR REPLACE INTO discord_replies (message_id, issue_number, comment_id) VALUES ($message, $issue, $comment)";
        insert.Parameters.AddWithValue("$message", (long)messageId);
        insert.Parameters.AddWithValue("$issue", issueNumber);
        insert.Parameters.AddWithValue("$comment", commentId);
        await insert.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteReplyAsync(ulong messageId, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _database.OpenAsync(cancellationToken);
        await using SqliteCommand delete = connection.CreateCommand();
        delete.CommandText = "DELETE FROM discord_replies WHERE message_id = $message";
        delete.Parameters.AddWithValue("$message", (long)messageId);
        await delete.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>The GitHub account a member linked with /link-github, or null.</summary>
    public async Task<string?> GetGitHubLoginAsync(ulong userId, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _database.OpenAsync(cancellationToken);
        await using SqliteCommand select = connection.CreateCommand();
        select.CommandText = "SELECT login FROM github_links WHERE user_id = $user";
        select.Parameters.AddWithValue("$user", (long)userId);
        return await select.ExecuteScalarAsync(cancellationToken) as string;
    }

    /// <summary>Link a member to a GitHub account, or unlink them when <paramref name="login"/> is null.</summary>
    public async Task SetGitHubLoginAsync(ulong userId, string? login, CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await _database.OpenAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = login is null
            ? "DELETE FROM github_links WHERE user_id = $user"
            : """
              INSERT INTO github_links (user_id, login, linked_utc) VALUES ($user, $login, $now)
              ON CONFLICT(user_id) DO UPDATE SET login = excluded.login, linked_utc = excluded.linked_utc;
              """;
        command.Parameters.AddWithValue("$user", (long)userId);
        command.Parameters.AddWithValue("$login", (object?)login ?? DBNull.Value);
        command.Parameters.AddWithValue("$now", Now());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static string Now() => DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
}
