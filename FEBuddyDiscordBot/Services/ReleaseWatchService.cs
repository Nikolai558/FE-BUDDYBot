using Discord.Net;
using FEBuddyDiscordBot.DataAccess;
using FEBuddyDiscordBot.Models;
using FEBuddyDiscordBot.Announcements;

namespace FEBuddyDiscordBot.Services;

/// <summary>
/// Checks GitHub for new FE-BUDDY releases every few minutes. Each new release at or above the level chosen with
/// /admin releases gets a preview in the review channel, where an admin posts, edits or cancels it
/// (see <see cref="ReleaseInteractionHandler"/>). Releases already out when the bot starts watching are never announced.
/// </summary>
public sealed class ReleaseWatchService : BackgroundService
{
    private readonly DiscordSocketClient _discord;
    private readonly GitHubApi _github;
    private readonly ReleaseStore _store;
    private readonly GuildSettingsStore _settings;
    private readonly BotHealth _health;
    private readonly GitHubOptions _options;
    private readonly ulong _guildId;
    private readonly ILogger<ReleaseWatchService> _logger;

    public ReleaseWatchService(
        DiscordSocketClient discord,
        GitHubApi github,
        ReleaseStore store,
        GuildSettingsStore settings,
        BotHealth health,
        IOptions<GitHubOptions> options,
        IOptions<BotOptions> botOptions,
        ILogger<ReleaseWatchService> logger)
    {
        _discord = discord;
        _github = github;
        _store = store;
        _settings = settings;
        _health = health;
        _options = options.Value;
        _guildId = botOptions.Value.GuildId;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_github.IsConfigured)
        {
            _logger.LogInformation("Releases: GitHub App not configured; release announcements are off");
            return;
        }

        using PeriodicTimer timer = new(TimeSpan.FromSeconds(Math.Max(60, _options.PollIntervalSeconds)));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            if (_discord.ConnectionState != ConnectionState.Connected || _discord.GetGuild(_guildId) is not { } guild) continue;

            try
            {
                await CheckAsync(guild, stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                // Ask for the full list next time rather than trusting "not modified".
                _github.ForgetReleasesETag();
                _health.RecordReleaseCheck(false, ex.Message);
                _logger.LogWarning(ex is GitHubException ? null : ex, "Releases: check failed: {Error}", ex.Message);
            }
        }
    }

    private async Task CheckAsync(SocketGuild guild, CancellationToken cancellationToken)
    {
        if (await _github.ListReleasesIfChangedAsync(cancellationToken) is not { } releases)
        {
            _health.RecordReleaseCheck(true, "no new releases");
            return;
        }

        HashSet<string> seen = await _store.GetSeenTagsAsync(cancellationToken);
        List<GitHubRelease> fresh = releases.Where(r => !seen.Contains(r.TagName)).OrderBy(r => r.PublishedAt).ToList();

        if (!seen.Contains(ReleaseStore.StartedMarker))
        {
            foreach (GitHubRelease release in fresh)
            {
                await _store.RecordSkippedAsync(release.TagName, ReleaseLevels.FromTag(release.TagName, release.Prerelease), cancellationToken);
            }

            await _store.RecordSkippedAsync(ReleaseStore.StartedMarker, ReleaseLevel.Stable, cancellationToken);
            _logger.LogInformation("Releases: started watching {Repository}; the {Count} releases already out won't be announced", _github.Repository, fresh.Count);
            _health.RecordReleaseCheck(true, $"started watching; {fresh.Count} existing releases skipped");
            return;
        }

        DateTimeOffset? started = await _store.GetWatchStartedAsync(cancellationToken);
        foreach (GitHubRelease release in fresh)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Only the newest releases were noted when watching started; an older one can surface later if a newer
            // one is deleted on GitHub. It was out before watching started, so it isn't new.
            if (release.PublishedAt < started)
            {
                await _store.RecordSkippedAsync(release.TagName, ReleaseLevels.FromTag(release.TagName, release.Prerelease), cancellationToken);
                continue;
            }

            await HandleNewAsync(guild, release, cancellationToken);
        }

        _health.RecordReleaseCheck(true, fresh.Count == 0 ? "no new releases" : $"{fresh.Count} new: {string.Join(", ", fresh.Select(r => r.Version))}");
    }

    private async Task HandleNewAsync(SocketGuild guild, GitHubRelease release, CancellationToken cancellationToken)
    {
        ReleaseLevel level = ReleaseLevels.FromTag(release.TagName, release.Prerelease);
        GuildSettings settings = _settings.Current;

        if (settings.ReleaseLevel is not ReleaseLevel wanted || level < wanted)
        {
            await _store.RecordSkippedAsync(release.TagName, level, cancellationToken);
            _logger.LogInformation("Releases: FE-BUDDY {Version} ({Level}) is out; not announced ({Setting})", release.Version, ReleaseLevels.Name(level),
                settings.ReleaseLevel is ReleaseLevel setting ? $"only {ReleaseLevels.Name(setting).ToLowerInvariant()} and up are" : "announcements are off");
            return;
        }

        if (settings.ReleaseReviewChannelId is not ulong reviewId || guild.GetTextChannel(reviewId) is not { } review)
        {
            await _store.RecordSkippedAsync(release.TagName, level, cancellationToken);
            _logger.LogWarning("Releases: FE-BUDDY {Version} is out but wasn't announced: set the review channel with /admin releases review-channel:", release.Version);
            return;
        }

        string? emoji = guild.Emotes.FirstOrDefault(e => e.Name == ReleaseAnnouncement.EmojiName)?.ToString();
        string text = ReleaseAnnouncement.Build(release.Version, level, release.HtmlUrl, release.Body, emoji);

        IUserMessage preview;
        try
        {
            preview = await review.SendMessageAsync(text,
                embed: ReleasePreview.PendingEmbed(release.Version, level, release.HtmlUrl, settings),
                components: ReleasePreview.Buttons(release.TagName),
                allowedMentions: AllowedMentions.None);
        }
        catch (HttpException ex) when (ex.DiscordCode == DiscordErrorCode.MissingPermissions || ex.HttpCode == System.Net.HttpStatusCode.Forbidden)
        {
            await _store.RecordSkippedAsync(release.TagName, level, cancellationToken);
            _logger.LogWarning("Releases: FE-BUDDY {Version} is out but wasn't announced: I can't post in #{Channel} ({Error})", release.Version, review.Name, ex.Message);
            return;
        }

        await _store.RecordPendingAsync(release.TagName, level, release.HtmlUrl, review.Id, preview.Id, cancellationToken);
        _logger.LogInformation("Releases: FE-BUDDY {Version} ({Level}) is out; preview posted in #{Channel} for an admin to post", release.Version, ReleaseLevels.Name(level), review.Name);
    }
}
