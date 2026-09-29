using System.Collections.Concurrent;
using System.Globalization;
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

    private static string Now() => DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture);
}
