using FEBuddyDiscordBot.DataAccess;
using FEBuddyDiscordBot.Models;

namespace FEBuddyDiscordBot.Services;

/// <summary>
/// Checks GitHub every few minutes and gives each open issue a forum post, including issues opened on GitHub.
/// The first check after the forum is set mirrors every open issue; closed issues are never backfilled.
/// </summary>
public sealed class IssueSyncService : BackgroundService
{
    private readonly DiscordSocketClient _discord;
    private readonly GitHubApi _github;
    private readonly IssueForumService _forum;
    private readonly IssueSubmissionService _submissions;
    private readonly GitHubOptions _options;
    private readonly ILogger<IssueSyncService> _logger;

    public IssueSyncService(
        DiscordSocketClient discord,
        GitHubApi github,
        IssueForumService forum,
        IssueSubmissionService submissions,
        IOptions<GitHubOptions> options,
        ILogger<IssueSyncService> logger)
    {
        _discord = discord;
        _github = github;
        _forum = forum;
        _submissions = submissions;
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
        // Keep the release list for the version dropdown fresh, so opening the form never waits on GitHub.
        await _submissions.GetVersionsAsync();

        IReadOnlyList<GitHubIssue>? open = await _github.ListOpenIssuesIfChangedAsync(cancellationToken);
        if (open is null) return;

        // Oldest first, so the forum's newest posts are the newest issues.
        foreach (GitHubIssue issue in open.OrderBy(i => i.Number))
        {
            cancellationToken.ThrowIfCancellationRequested();
            await _forum.EnsurePostAsync(issue);
        }
    }
}
