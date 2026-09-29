using FEBuddyDiscordBot.DataAccess;
using FEBuddyDiscordBot.Models;

namespace FEBuddyDiscordBot.Services;

/// <summary>
/// Checks GitHub every few minutes and keeps the issue forum in step: a post for each open issue (including issues
/// opened on GitHub), GitHub comments copied into posts, and titles, tags and open/closed state updated.
/// The first check after the forum is set mirrors every open issue; closed issues are never backfilled.
/// Each check asks only for what changed since the last one (two requests), plus an ETag check of the open list.
/// </summary>
public sealed class IssueSyncService : BackgroundService
{
    private readonly DiscordSocketClient _discord;
    private readonly GitHubApi _github;
    private readonly IssueForumService _forum;
    private readonly IssueSubmissionService _submissions;
    private readonly IssueStore _store;
    private readonly GitHubOptions _options;
    private readonly ILogger<IssueSyncService> _logger;

    public IssueSyncService(
        DiscordSocketClient discord,
        GitHubApi github,
        IssueForumService forum,
        IssueSubmissionService submissions,
        IssueStore store,
        IOptions<GitHubOptions> options,
        ILogger<IssueSyncService> logger)
    {
        _discord = discord;
        _github = github;
        _forum = forum;
        _submissions = submissions;
        _store = store;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_github.IsConfigured)
        {
            _logger.LogInformation("Issues: GitHub App not configured; issue sync is off");
            return;
        }

        using PeriodicTimer timer = new(TimeSpan.FromSeconds(Math.Max(30, _options.PollIntervalSeconds)));
        do
        {
            if (_discord.ConnectionState != ConnectionState.Connected || _forum.Forum is null) continue;

            try
            {
                await SyncAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                // Check everything again next time rather than trusting "not modified".
                _github.ForgetOpenIssuesETag();
                _logger.LogWarning("Issues: sync with GitHub failed: {Error}", ex is GitHubException ? ex.Message : ex.ToString());
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task SyncAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset started = DateTimeOffset.UtcNow;

        // Keep the release list for the version dropdown fresh, so opening the form never waits on GitHub.
        await _submissions.GetVersionsAsync(timeout: TimeSpan.FromSeconds(15));

        // New open issues get posts. Oldest first, so the forum's newest posts are the newest issues.
        if (await _github.ListOpenIssuesIfChangedAsync(cancellationToken) is { } open)
        {
            foreach (GitHubIssue issue in open.OrderBy(i => i.Number))
            {
                cancellationToken.ThrowIfCancellationRequested();
                await _forum.EnsurePostAsync(issue);
            }
        }

        // Then everything that changed since the last check. The first time, start from the oldest post,
        // so changes made since posts were created are caught but older history isn't replayed.
        DateTimeOffset since = await _store.GetSyncCursorAsync(cancellationToken)
                               ?? await _store.GetOldestPostTimeAsync(cancellationToken)
                               ?? started;

        List<GitHubComment> comments = await _github.ListCommentsUpdatedSinceAsync(since, cancellationToken);
        List<GitHubIssue> issues = await _github.ListIssuesUpdatedSinceAsync(since, cancellationToken);

        // Comments before issue changes, so a closing comment lands before the "closed" message.
        int failures = 0;
        foreach (GitHubComment comment in comments)
        {
            failures += await TryAsync(() => _forum.SyncCommentAsync(comment), $"comment {comment.Id} on #{comment.IssueNumber}", cancellationToken);
        }

        foreach (GitHubIssue issue in issues)
        {
            failures += await TryAsync(() => _forum.SyncPostAsync(issue), $"issue #{issue.Number}", cancellationToken);
        }

        // Redoing things is harmless (the bot compares with what each post shows), so after a failure check the
        // same window again next time. But don't let one stuck post hold everything back for more than an hour.
        if (failures == 0 || started - since > TimeSpan.FromHours(1))
        {
            // A minute's overlap covers changes GitHub hadn't finished indexing when we asked.
            await _store.SetSyncCursorAsync(started.AddMinutes(-1), cancellationToken);
        }
    }

    /// <summary>Run one item's sync; returns 1 if it failed (logged), 0 if it worked.</summary>
    private async Task<int> TryAsync(Func<Task> sync, string what, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            await sync();
            return 0;
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogWarning("Issues: syncing {What} failed: {Error}", what, ex is GitHubException ? ex.Message : ex.ToString());
            return 1;
        }
    }
}
