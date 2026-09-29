using System.Globalization;
using System.Security.Cryptography;
using System.Text;
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
        string content = draft is not null
            ? $"Reported by <@{draft.UserId}>"
            : $"Opened on GitHub by **{issue.User?.Login ?? "unknown"}**";

        MessageComponent buttons = new ComponentBuilder()
            .WithButton("Open on GitHub", style: ButtonStyle.Link, url: issue.HtmlUrl)
            .Build();

        PostState state = StateOf(issue);
        Embed embed = BuildStarterEmbed(issue, withAvatar: draft is null);
        ForumTag[] tags = TagObjects(forum, state.Tags);
        AllowedMentions mentions = draft is null ? AllowedMentions.None : new AllowedMentions { UserIds = [draft.UserId] };

        IThreadChannel thread = files.Count == 0
            ? await forum.CreatePostAsync(state.Title, ThreadArchiveDuration.OneWeek, text: content, embed: embed,
                allowedMentions: mentions, components: buttons, tags: tags)
            : await forum.CreatePostWithFilesAsync(state.Title, files, ThreadArchiveDuration.OneWeek, text: content, embed: embed,
                allowedMentions: mentions, components: buttons, tags: tags);

        await _store.SavePostAsync(issue.Number, thread.Id, forum.Id, draft?.UserId);
        await _store.SavePostStateAsync(issue.Number, state);
        _logger.LogInformation("Issues: created forum post {ThreadId} for issue #{Number}", thread.Id, issue.Number);
        return thread.Id;
    }

    // ---- Keeping posts in step with GitHub ----

    /// <summary>
    /// Bring an issue's post up to date: title, tags, the first message's text, and closed (locked and archived)
    /// or open. Does nothing if the issue has no post in the forum, or nothing the post shows has changed.
    /// </summary>
    public async Task SyncPostAsync(GitHubIssue issue)
    {
        if (Forum is not { } forum || !_store.HasPostIn(issue.Number, forum.Id)) return;

        PostState state = StateOf(issue);
        PostState? previous = await _store.GetPostStateAsync(issue.Number);
        if (previous is not null && previous.Matches(state)) return;

        if (await GetThreadAsync(issue.Number) is not { } thread) return;

        // Archived posts can't be changed or posted in until they're unarchived. Closed posts are archived again below.
        if (thread.IsArchived) await thread.ModifyAsync(p => p.Archived = false);

        if ((previous?.BodyHash != state.BodyHash || previous.Title != state.Title)
            && await thread.GetMessageAsync(thread.Id) is IUserMessage starter && starter.Author.Id == _discord.CurrentUser.Id)
        {
            bool withAvatar = starter.Embeds.FirstOrDefault()?.Thumbnail is not null;
            await starter.ModifyAsync(m => m.Embed = BuildStarterEmbed(issue, withAvatar));
        }

        bool wasOpen = previous?.IsOpen ?? true;
        if (wasOpen && !state.IsOpen)
        {
            await thread.SendMessageAsync(await ClosedMessageAsync(issue, state), allowedMentions: AllowedMentions.None);
        }
        else if (!wasOpen && state.IsOpen)
        {
            await thread.SendMessageAsync("🟢 **Reopened** on GitHub. This post is open again.", allowedMentions: AllowedMentions.None);
        }

        ulong[] tagIds = TagObjects(forum, state.Tags).Select(t => t.Id).ToArray();
        // Discord only allows a couple of renames every 10 minutes, so only rename when the title changed.
        bool renamed = previous?.Title != state.Title;
        await thread.ModifyAsync(p =>
        {
            if (renamed) p.Name = state.Title;
            p.AppliedTags = tagIds;
            p.Locked = !state.IsOpen;
        });
        if (!state.IsOpen) await thread.ModifyAsync(p => p.Archived = true);

        await _store.SavePostStateAsync(issue.Number, state);
        _logger.LogInformation("Issues: updated the forum post for issue #{Number} ({State})", issue.Number, state.IsOpen ? "open" : "closed");
    }

    /// <summary>
    /// Copy a GitHub comment into its issue's post, or update the copy if the comment was edited.
    /// Comments the bot made itself (copies of Discord messages) are skipped.
    /// </summary>
    public async Task SyncCommentAsync(GitHubComment comment)
    {
        if (Forum is not { } forum || !_store.HasPostIn(comment.IssueNumber, forum.Id)) return;
        if (comment.PerformedViaGitHubApp?.Id == _github.AppId) return;

        (ulong MessageId, DateTimeOffset UpdatedUtc)? copied = await _store.GetCommentAsync(comment.Id);
        if (copied is { } c && c.UpdatedUtc >= comment.UpdatedAt) return;

        if (await GetThreadAsync(comment.IssueNumber) is not { } thread) return;

        // Closed posts are locked and archived; the bot (with Manage Posts) can still post in them once unarchived.
        bool wasArchived = thread.IsArchived;
        if (wasArchived) await thread.ModifyAsync(p => p.Archived = false);

        Embed embed = new EmbedBuilder()
            .WithAuthor(comment.User?.Login ?? "GitHub user", comment.User?.AvatarUrl, comment.HtmlUrl)
            .WithDescription(IssueText.ForDiscord(comment.Body, 4000))
            .WithColor(new Color(0x24292F))
            .WithFooter("Comment on GitHub")
            .WithTimestamp(comment.CreatedAt)
            .Build();

        ulong messageId;
        if (copied is { } existing && await thread.GetMessageAsync(existing.MessageId) is IUserMessage message)
        {
            await message.ModifyAsync(m => m.Embed = embed);
            messageId = message.Id;
        }
        else
        {
            messageId = (await thread.SendMessageAsync(embed: embed, allowedMentions: AllowedMentions.None)).Id;
        }

        if (wasArchived && thread.IsLocked) await thread.ModifyAsync(p => p.Archived = true);

        await _store.SaveCommentAsync(comment.Id, comment.IssueNumber, messageId, comment.UpdatedAt);
        _logger.LogInformation("Issues: {Action} GitHub comment {CommentId} on issue #{Number}", copied is null ? "copied" : "updated", comment.Id, comment.IssueNumber);
    }

    /// <summary>
    /// The issue's post, fetched from Discord if it isn't cached (archived posts aren't). If the post was deleted,
    /// the bot forgets it, so it's made again while the issue is open.
    /// </summary>
    private async Task<IThreadChannel?> GetThreadAsync(int issueNumber)
    {
        if (_store.GetPostId(issueNumber) is not ulong threadId) return null;

        try
        {
            if (await _discord.GetChannelAsync(threadId) is IThreadChannel thread) return thread;
        }
        catch (Discord.Net.HttpException ex) when (ex.HttpCode == System.Net.HttpStatusCode.NotFound)
        {
        }

        _logger.LogWarning("Issues: the forum post for issue #{Number} was deleted; it will be made again if the issue is open", issueNumber);
        await _store.ForgetPostAsync(issueNumber);
        _github.ForgetOpenIssuesETag();
        return null;
    }

    private async Task<string> ClosedMessageAsync(GitHubIssue issue, PostState state)
    {
        string reason = state.Tags.Contains(IssueTags.Duplicate) ? "🔁 Closed as a **duplicate**"
            : state.Tags.Contains(IssueTags.Invalid) ? "🚫 Closed as **invalid**"
            : state.Tags.Contains(IssueTags.WontFix) ? "🚫 Closed as **not planned**"
            : "✅ Closed as **completed**";

        // The issue list doesn't say who closed it; the single issue does.
        string? closedBy = null;
        try
        {
            closedBy = (await _github.GetIssueAsync(issue.Number)).ClosedBy?.Login;
        }
        catch (GitHubException ex)
        {
            _logger.LogDebug("Issues: couldn't look up who closed #{Number}: {Error}", issue.Number, ex.Message);
        }

        return $"{reason} on GitHub{(closedBy is null ? "" : $" by **{closedBy}**")}. " +
               "This post is now locked. If the issue is reopened on GitHub, it reopens here too.";
    }

    private Embed BuildStarterEmbed(GitHubIssue issue, bool withAvatar)
    {
        EmbedBuilder embed = new EmbedBuilder()
            .WithTitle(issue.Title.Length > 256 ? issue.Title[..255] + "…" : issue.Title)
            .WithUrl(issue.HtmlUrl)
            .WithDescription(IssueText.ForDiscord(issue.Body))
            .WithColor(new Color(0x24292F))
            .WithFooter($"{_github.Repository}#{issue.Number}");

        if (withAvatar && issue.User?.AvatarUrl is string avatar) embed.WithThumbnailUrl(avatar);
        return embed.Build();
    }

    /// <summary>What the issue's post should show.</summary>
    private static PostState StateOf(GitHubIssue issue)
    {
        string title = $"#{issue.Number} {issue.Title}";
        if (title.Length > MaxPostTitle) title = title[..(MaxPostTitle - 1)] + "…";

        string[] tags = IssueTags.ForIssue(issue.LabelNames, issue.Title, issue.Body, issue.IsOpen, issue.StateReason).ToArray();
        string bodyHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(issue.Body ?? "")));

        return new PostState(title, issue.IsOpen, tags, bodyHash);
    }

    private static ForumTag[] TagObjects(SocketForumChannel forum, IEnumerable<string> names) =>
        names
            .Select(name => forum.Tags.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.OrdinalIgnoreCase)))
            .Where(t => t.Id != 0)
            .ToArray();
}
