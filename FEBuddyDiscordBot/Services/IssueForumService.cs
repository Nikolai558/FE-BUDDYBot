using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FEBuddyDiscordBot.DataAccess;
using FEBuddyDiscordBot.Issues;
using FEBuddyDiscordBot.Models;

namespace FEBuddyDiscordBot.Services;

/// <summary>
/// Keeps the issue forum in step with GitHub: one bot-owned post per issue, tagged from the issue's labels.
/// </summary>
public sealed class IssueForumService
{
    public const string DiscordApiHttpClientName = "discord-api";

    /// <summary>Forum post titles can be at most 100 characters.</summary>
    private const int MaxPostTitle = 100;

    private readonly DiscordSocketClient _discord;
    private readonly GuildSettingsStore _settings;
    private readonly IssueStore _store;
    private readonly GitHubApi _github;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly BotOptions _options;
    private readonly ILogger<IssueForumService> _logger;

    // One post per issue: a submission holds this from creating the issue until its post exists,
    // so the GitHub poller can't make a second post for it in between.
    private readonly SemaphoreSlim _postLock = new(1, 1);

    public IssueForumService(
        DiscordSocketClient discord,
        GuildSettingsStore settings,
        IssueStore store,
        GitHubApi github,
        IHttpClientFactory httpClientFactory,
        IOptions<BotOptions> options,
        ILogger<IssueForumService> logger)
    {
        _discord = discord;
        _settings = settings;
        _store = store;
        _github = github;
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
    }

    public SocketGuild? Guild => _discord.GetGuild(_options.GuildId);

    public SocketForumChannel? Forum =>
        _settings.Current.IssueForumChannelId is ulong id ? Guild?.GetForumChannel(id) : null;

    public string PostUrl(ulong threadId) => $"https://discord.com/channels/{_options.GuildId}/{threadId}";

    /// <summary>
    /// Add any of the <see cref="IssueTags"/> the forum is missing and make them all moderated (only the bot and
    /// moderators can apply them). The forum's other tags are kept as they are.
    /// Returns a problem to show the admin, or null if the tags are all in place.
    /// </summary>
    public async Task<string?> EnsureTagsAsync(SocketForumChannel forum)
    {
        bool IsOurs(ForumTag tag) => IssueTags.All.Any(d => string.Equals(d.Name, tag.Name, StringComparison.OrdinalIgnoreCase));

        List<ForumTagDefinition> missing = IssueTags.All
            .Where(d => !forum.Tags.Any(t => string.Equals(t.Name, d.Name, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        if (missing.Count == 0 && forum.Tags.Where(IsOurs).All(t => t.IsModerated)) return null;

        if (forum.Tags.Count + missing.Count > 20)
        {
            return $"The forum needs {missing.Count} more tags but Discord allows 20. Remove some of its other tags, then run this again.";
        }

        if (!Guild!.CurrentUser.GetPermissions(forum).ManageChannel)
        {
            return $"I need **Manage Channel** on {forum.Mention} to add its tags ({string.Join(", ", missing.Select(m => m.Name))}).";
        }

        // Discord's tag objects: existing tags keep their ID (and emoji); new ones have none.
        List<Dictionary<string, object?>> tags = forum.Tags
            .Select(t => new Dictionary<string, object?>
            {
                ["id"] = t.Id.ToString(CultureInfo.InvariantCulture),
                ["name"] = t.Name,
                ["moderated"] = t.IsModerated || IsOurs(t),
                ["emoji_id"] = (t.Emoji as Emote)?.Id.ToString(CultureInfo.InvariantCulture),
                ["emoji_name"] = (t.Emoji as Emoji)?.Name,
            })
            .Concat(missing.Select(m => new Dictionary<string, object?>
            {
                ["name"] = m.Name,
                ["moderated"] = true,
                ["emoji_name"] = m.Emoji,
            }))
            .ToList();

        // Discord.Net's forum ModifyAsync drops each tag's "moderated" setting and resets the forum's flags
        // (e.g. "Require tags"), so send only the tag list to Discord directly.
        HttpClient http = _httpClientFactory.CreateClient(DiscordApiHttpClientName);
        using HttpRequestMessage request = new(HttpMethod.Patch, $"channels/{forum.Id}")
        {
            Content = JsonContent.Create(new { available_tags = tags }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bot", _options.Token);
        request.Headers.Add("X-Audit-Log-Reason", "FE-BUDDY issue tags");

        using HttpResponseMessage response = await http.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            string detail = await response.Content.ReadAsStringAsync();
            _logger.LogWarning("Issues: Discord refused the forum tags: HTTP {Status} {Detail}", (int)response.StatusCode, detail);
            return $"Discord refused the forum's tags (HTTP {(int)response.StatusCode}). Details are in the bot's log.";
        }

        _logger.LogInformation("Issues: added {Count} tags to the issue forum", missing.Count);
        return null;
    }

    /// <summary>
    /// Create an issue on GitHub and its forum post, as one step no other post can sneak into.
    /// If the issue is created but the post isn't, the thread ID is null and the next GitHub sync makes the post.
    /// </summary>
    public async Task<(GitHubIssue Issue, ulong? ThreadId)> CreateIssueWithPostAsync(
        Func<Task<GitHubIssue>> createIssue, IssueDraft draft, IReadOnlyList<FileAttachment> files)
    {
        SocketForumChannel forum = Forum ?? throw new GitHubException("The issue forum isn't set. Use /admin issues.");

        await _postLock.WaitAsync();
        try
        {
            GitHubIssue issue = await createIssue();
            try
            {
                return (issue, await CreatePostAsync(forum, issue, draft, files));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Issues: created issue #{Number} but not its forum post", issue.Number);
                return (issue, null);
            }
        }
        finally
        {
            _postLock.Release();
        }
    }

    /// <summary>Give an issue that was opened on GitHub its forum post, if it doesn't have one yet.</summary>
    public async Task EnsurePostAsync(GitHubIssue issue)
    {
        if (Forum is not { } forum || _store.HasPostIn(issue.Number, forum.Id)) return;

        await _postLock.WaitAsync();
        try
        {
            if (_store.HasPostIn(issue.Number, forum.Id)) return;
            await CreatePostAsync(forum, issue, draft: null, files: []);
        }
        finally
        {
            _postLock.Release();
        }
    }

    private async Task<ulong> CreatePostAsync(SocketForumChannel forum, GitHubIssue issue, IssueDraft? draft, IReadOnlyList<FileAttachment> files)
    {
        string title = $"#{issue.Number} {issue.Title}";
        if (title.Length > MaxPostTitle) title = title[..(MaxPostTitle - 1)] + "…";

        EmbedBuilder embed = new EmbedBuilder()
            .WithTitle(issue.Title.Length > 256 ? issue.Title[..255] + "…" : issue.Title)
            .WithUrl(issue.HtmlUrl)
            .WithDescription(IssueText.ForDiscord(issue.Body))
            .WithColor(new Color(0x24292F))
            .WithFooter($"{_github.Repository}#{issue.Number}");

        string content;
        if (draft is not null)
        {
            content = $"Reported by <@{draft.UserId}>";
        }
        else
        {
            content = $"Opened on GitHub by **{issue.User?.Login ?? "unknown"}**";
            if (issue.User?.AvatarUrl is string avatar) embed.WithThumbnailUrl(avatar);
        }

        MessageComponent buttons = new ComponentBuilder()
            .WithButton("Open on GitHub", style: ButtonStyle.Link, url: issue.HtmlUrl)
            .Build();

        ForumTag[] tags = TagsFor(forum, issue);
        AllowedMentions mentions = draft is null ? AllowedMentions.None : new AllowedMentions { UserIds = [draft.UserId] };

        IThreadChannel thread = files.Count == 0
            ? await forum.CreatePostAsync(title, ThreadArchiveDuration.OneWeek, text: content, embed: embed.Build(),
                allowedMentions: mentions, components: buttons, tags: tags)
            : await forum.CreatePostWithFilesAsync(title, files, ThreadArchiveDuration.OneWeek, text: content, embed: embed.Build(),
                allowedMentions: mentions, components: buttons, tags: tags);

        await _store.SavePostAsync(issue.Number, thread.Id, forum.Id, draft?.UserId);
        _logger.LogInformation("Issues: created forum post {ThreadId} for issue #{Number}", thread.Id, issue.Number);
        return thread.Id;
    }

    private static ForumTag[] TagsFor(SocketForumChannel forum, GitHubIssue issue) =>
        IssueTags.ForIssue(issue.LabelNames, issue.Title, issue.Body, issue.IsOpen, issue.StateReason)
            .Select(name => forum.Tags.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)))
            .Where(t => t.Id != 0)
            .ToArray();
}
