using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Discord.Net;
using FEBuddyDiscordBot.DataAccess;
using FEBuddyDiscordBot.Issues;
using FEBuddyDiscordBot.Models;

namespace FEBuddyDiscordBot.Services;

public enum SubmitOutcome
{
    Created,
    AwaitingApproval,
    Failed,
}

/// <param name="ThreadId">The forum post, or null if the issue was created but its post wasn't (the next sync makes it).</param>
/// <param name="Notes">Things the member should know, e.g. files that were too big to copy.</param>
public sealed record SubmitResult(SubmitOutcome Outcome, GitHubIssue? Issue = null, ulong? ThreadId = null, string? Error = null, IReadOnlyList<string>? Notes = null);

/// <summary>
/// Everything behind the "report an issue" flow that isn't Discord UI: who may submit, drafts between the two
/// modals, the duplicate check, the hourly limit and approval queue, and creating the issue and its post.
/// </summary>
public sealed class IssueSubmissionService
{
    public const string FilesHttpClientName = "discord-files";

    /// <summary>Bots can upload files of up to 10 MB to servers without boosts.</summary>
    private const int MaxCopiedFileBytes = 10 * 1024 * 1024;

    private static readonly TimeSpan DraftLifetime = TimeSpan.FromHours(1);
    private static readonly TimeSpan ReleaseCacheLifetime = TimeSpan.FromMinutes(10);

    private readonly GuildSettingsStore _settings;
    private readonly IssueStore _store;
    private readonly GitHubApi _github;
    private readonly IssueForumService _forum;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<IssueSubmissionService> _logger;

    private readonly ConcurrentDictionary<ulong, IssueDraft> _drafts = new();
    private (DateTimeOffset Fetched, IReadOnlyList<string> Versions)? _releases;
    private DateTimeOffset _releasesRetryAfter;

    public IssueSubmissionService(
        GuildSettingsStore settings,
        IssueStore store,
        GitHubApi github,
        IssueForumService forum,
        IHttpClientFactory httpClientFactory,
        ILogger<IssueSubmissionService> logger)
    {
        _settings = settings;
        _store = store;
        _github = github;
        _forum = forum;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    // ---- Who may do what ----

    /// <summary>Why this member can't submit this kind of issue, or null if they can.</summary>
    public string? CantSubmit(SocketGuildUser member, IssueTemplate template)
    {
        GuildSettings s = _settings.Current;
        if (!_github.IsConfigured || _forum.Forum is null) return "Issue reporting isn't set up yet.";

        bool isAdmin = member.GuildPermissions.ManageGuild;
        if (!isAdmin && !(s.VerifiedRoleId is ulong verified && HasRole(member, verified)))
        {
            return "You need the **Verified** role to report issues. Run `/give-role` to get it.";
        }

        if (template.RestrictedToDevTaskRoles && !isAdmin && !(s.DevTaskRoleIds ?? []).Any(id => HasRole(member, id)))
        {
            return "Development tasks can only be added by contributors and project managers.";
        }

        return null;
    }

    public bool CanApprove(SocketGuildUser member) =>
        member.GuildPermissions.ManageGuild
        || (_settings.Current.IssueApproverRoleId is ulong role && HasRole(member, role));

    private static bool HasRole(SocketGuildUser member, ulong roleId) => member.Roles.Any(r => r.Id == roleId);

    // ---- Drafts ----

    public void SaveDraft(IssueDraft draft) => _drafts[draft.UserId] = draft;

    public IssueDraft? GetDraft(ulong userId) =>
        _drafts.TryGetValue(userId, out IssueDraft? draft) && DateTimeOffset.UtcNow - draft.StartedUtc < DraftLifetime ? draft : null;

    public void DiscardDraft(ulong userId) => _drafts.TryRemove(userId, out _);

    // ---- GitHub lookups ----

    /// <summary>Existing issues that look like this title, best match first. Empty if GitHub can't be searched.</summary>
    public async Task<IReadOnlyList<GitHubIssue>> FindDuplicatesAsync(string title)
    {
        if (IssueText.SearchTerms(title) is not string terms) return [];

        try
        {
            return await _github.SearchIssuesAsync(terms, 5);
        }
        catch (GitHubException ex)
        {
            _logger.LogWarning("Issues: duplicate search failed: {Error}", ex.Message);
            return [];
        }
    }

    /// <summary>
    /// FE-BUDDY's latest release versions for the version dropdown, or null if they aren't available.
    /// Discord only waits 3 seconds for the modal, so this never waits long: after a failure it doesn't
    /// ask GitHub again for a minute. The background sync keeps the list fresh.
    /// </summary>
    public async Task<IReadOnlyList<string>?> GetVersionsAsync()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        if (_releases is { } cached && now - cached.Fetched < ReleaseCacheLifetime) return cached.Versions;
        if (now < _releasesRetryAfter) return _releases?.Versions;

        try
        {
            using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(1.5));
            IReadOnlyList<GitHubRelease> releases = await _github.ListReleasesAsync(20, timeout.Token);
            if (releases.Count == 0) return null;

            List<string> versions = releases.Select(r => r.TagName.TrimStart('v', 'V')).ToList();
            _releases = (now, versions);
            return versions;
        }
        catch (Exception ex) when (ex is GitHubException or OperationCanceledException)
        {
            _releasesRetryAfter = now.AddMinutes(1);
            _logger.LogWarning("Issues: couldn't load FE-BUDDY releases: {Error}", ex.Message);
            return _releases?.Versions;
        }
    }

    // ---- Submitting ----

    /// <summary>
    /// Submit a finished draft: create the issue and its post, or queue it for approval when the member is over the hourly limit.
    /// </summary>
    public async Task<SubmitResult> SubmitAsync(IssueDraft draft, SocketGuildUser member, IReadOnlyCollection<IAttachment> attachments)
    {
        List<string> notes = [];
        List<DownloadedFile> files = await DownloadAsync(attachments, notes);
        draft.Attachments = files.Select(f => f.ToDraftAttachment()).ToList();

        GuildSettings s = _settings.Current;
        int recent = await _store.CountSubmissionsSinceAsync(member.Id, DateTimeOffset.UtcNow.AddHours(-1));
        if (recent >= s.IssueSubmissionsPerHour && !CanApprove(member))
        {
            return await QueueForApprovalAsync(draft, member, files, recent, notes);
        }

        SubmitResult result = await CreateAsync(draft, files, submissionId: null);
        return result with { Notes = notes };
    }

    /// <summary>
    /// Create the issue and its post. Once the issue exists this always reports success, even if a later
    /// step fails, so the member isn't told to try again and make a duplicate.
    /// </summary>
    private async Task<SubmitResult> CreateAsync(IssueDraft draft, IReadOnlyList<DownloadedFile> files, long? submissionId)
    {
        List<FileAttachment> uploads = files.Select(f => new FileAttachment(new MemoryStream(f.Bytes), f.FileName)).ToList();
        GitHubIssue issue;
        ulong? threadId;
        try
        {
            (issue, threadId) = await _forum.CreateIssueWithPostAsync(
                () => _github.CreateIssueAsync(draft.FullTitle, IssueText.BuildBody(draft, postUrl: null), draft.Template.Labels),
                draft,
                uploads);
        }
        catch (GitHubException ex)
        {
            _logger.LogWarning("Issues: creating an issue for {User} ({UserId}) failed: {Error}", draft.UserName, draft.UserId, ex.Message);
            return new SubmitResult(SubmitOutcome.Failed, Error: "GitHub didn't accept the issue. Please try again in a few minutes.");
        }
        finally
        {
            foreach (FileAttachment upload in uploads) upload.Dispose();
        }

        _logger.LogInformation("Issues: {User} ({UserId}) created issue #{Number}", draft.UserName, draft.UserId, issue.Number);

        try
        {
            if (submissionId is long id) await _store.SetIssueNumberAsync(id, issue.Number);
            else await _store.AddSubmissionAsync(draft.UserId, SubmissionStatus.Created, JsonSerializer.Serialize(draft), issue.Number);

            if (threadId is ulong thread) await _github.UpdateIssueBodyAsync(issue.Number, IssueText.BuildBody(draft, _forum.PostUrl(thread)));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Issues: issue #{Number} was created, but recording it or adding its Discord link failed", issue.Number);
        }

        return new SubmitResult(SubmitOutcome.Created, issue, threadId);
    }

    private async Task<SubmitResult> QueueForApprovalAsync(IssueDraft draft, SocketGuildUser member, IReadOnlyList<DownloadedFile> files, int recent, List<string> notes)
    {
        GuildSettings s = _settings.Current;
        if (s.IssueApprovalChannelId is not ulong channelId || _forum.Guild?.GetTextChannel(channelId) is not { } channel)
        {
            return new SubmitResult(SubmitOutcome.Failed, Error: $"You can submit {s.IssueSubmissionsPerHour} per hour. Please try again later.");
        }

        long id = await _store.AddSubmissionAsync(draft.UserId, SubmissionStatus.Pending, JsonSerializer.Serialize(draft), issueNumber: null);

        string ping = s.IssueApproverRoleId is ulong role ? $"<@&{role}> " : "";
        string content = $"{ping}{member.Mention} has already submitted {recent} issue{(recent == 1 ? "" : "s")} in the last hour. Approve this one?";
        AllowedMentions mentions = s.IssueApproverRoleId is ulong r ? new AllowedMentions { RoleIds = [r] } : AllowedMentions.None;

        Embed embed = new EmbedBuilder()
            .WithTitle(Truncate(draft.FullTitle, 256))
            .WithDescription(IssueText.ForDiscord(IssueText.BuildBody(draft, postUrl: null)))
            .WithAuthor(member.DisplayName, member.GetDisplayAvatarUrl())
            .WithColor(Color.Orange)
            .WithFooter($"Submission {id} · {draft.Template.Name}")
            .Build();

        MessageComponent buttons = new ComponentBuilder()
            .WithButton("Approve", $"issue:approve:{id}", ButtonStyle.Success)
            .WithButton("Deny", $"issue:deny:{id}", ButtonStyle.Danger)
            .Build();

        // Files go on the approval message so they're still there (with fresh links) when someone approves it.
        List<FileAttachment> uploads = files.Select(f => new FileAttachment(new MemoryStream(f.Bytes), f.FileName)).ToList();
        try
        {
            IUserMessage message = uploads.Count == 0
                ? await channel.SendMessageAsync(content, embed: embed, allowedMentions: mentions, components: buttons)
                : await channel.SendFilesAsync(uploads, content, embed: embed, allowedMentions: mentions, components: buttons);
            await _store.SetApprovalMessageAsync(id, message.Id);
        }
        catch (Exception ex)
        {
            // Without its approval message nobody could ever act on it; drop it so it doesn't count against the member.
            _logger.LogError(ex, "Issues: couldn't post submission {Id} for approval", id);
            await _store.TryResolveAsync(id, SubmissionStatus.Denied);
            return new SubmitResult(SubmitOutcome.Failed, Error: "I couldn't send this to the admins for approval. Please try again later.");
        }
        finally
        {
            foreach (FileAttachment upload in uploads) upload.Dispose();
        }

        _logger.LogInformation("Issues: submission {Id} from {User} ({UserId}) is waiting for approval", id, draft.UserName, draft.UserId);
        return new SubmitResult(SubmitOutcome.AwaitingApproval, Notes: notes);
    }

    // ---- Approvals ----

    /// <summary>
    /// Approve a waiting submission. <c>Resolved</c> is false when nothing changed (already handled, or creating
    /// the issue failed and it's still waiting); the message says why.
    /// </summary>
    public async Task<(bool Resolved, string Message)> ApproveAsync(long submissionId, SocketGuildUser approver)
    {
        if (await _store.GetSubmissionAsync(submissionId) is not { } submission) return (false, "This submission no longer exists.");
        if (!await _store.TryResolveAsync(submissionId, SubmissionStatus.Created)) return (false, "This submission was already handled.");

        IssueDraft draft;
        SubmitResult result;
        try
        {
            draft = JsonSerializer.Deserialize<IssueDraft>(submission.DraftJson)!;
            List<DownloadedFile> files = await DownloadApprovalFilesAsync(submission);
            draft.Attachments = files.Select(f => f.ToDraftAttachment()).ToList();
            result = await CreateAsync(draft, files, submissionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Issues: approving submission {Id} failed", submissionId);
            result = new SubmitResult(SubmitOutcome.Failed, Error: "Something went wrong creating the issue.");
            draft = null!;
        }

        if (result.Outcome != SubmitOutcome.Created)
        {
            await _store.ReopenAsync(submissionId);
            return (false, $"{result.Error} The submission is still waiting; try again in a few minutes.");
        }

        _logger.LogInformation("Issues: {Approver} approved submission {Id} → #{Number}", approver.Username, submissionId, result.Issue!.Number);
        await NotifyAsync(draft.UserId, $"Your {draft.Template.Name.ToLowerInvariant()} was approved: {Link(result)}");
        return (true, $"✅ Approved by {approver.Mention}: {Link(result)}");
    }

    public async Task<(bool Resolved, string Message)> DenyAsync(long submissionId, SocketGuildUser approver)
    {
        if (await _store.GetSubmissionAsync(submissionId) is not { } submission) return (false, "This submission no longer exists.");
        if (!await _store.TryResolveAsync(submissionId, SubmissionStatus.Denied)) return (false, "This submission was already handled.");

        IssueDraft draft = JsonSerializer.Deserialize<IssueDraft>(submission.DraftJson)!;
        _logger.LogInformation("Issues: {Approver} denied submission {Id}", approver.Username, submissionId);
        await NotifyAsync(draft.UserId, $"Your {draft.Template.Name.ToLowerInvariant()} \"{draft.Title}\" wasn't approved by the FE-BUDDY admins.");
        return (true, $"❌ Denied by {approver.Mention}.");
    }

    public string Link(SubmitResult result) => result.ThreadId is ulong thread
        ? $"[#{result.Issue!.Number}]({result.Issue.HtmlUrl}) · <#{thread}>"
        : $"[#{result.Issue!.Number}]({result.Issue.HtmlUrl})";

    private async Task NotifyAsync(ulong userId, string message)
    {
        if (_forum.Guild?.GetUser(userId) is not { } member) return;
        try
        {
            await member.SendMessageAsync(message);
        }
        catch (HttpException ex)
        {
            // Members can turn off DMs from server members.
            _logger.LogDebug("Issues: couldn't DM {UserId}: {Error}", userId, ex.Message);
        }
    }

    // ---- Files ----

    private sealed record DownloadedFile(string FileName, string? ContentType, byte[] Bytes, string Url)
    {
        // Text files were already redacted when downloaded.
        public DraftAttachment ToDraftAttachment() => new(FileName, ContentType, Bytes.Length, Url,
            IssueText.CanInline(FileName, ContentType, Bytes.Length) ? Encoding.UTF8.GetString(Bytes) : null);
    }

    private async Task<List<DownloadedFile>> DownloadAsync(IEnumerable<IAttachment> attachments, List<string> notes)
    {
        HttpClient http = _httpClientFactory.CreateClient(FilesHttpClientName);
        List<DownloadedFile> files = [];

        foreach (IAttachment attachment in attachments)
        {
            if (attachment.Size > MaxCopiedFileBytes)
            {
                notes.Add($"`{attachment.Filename}` is over 10 MB, so it wasn't included. Post it in the issue's forum post instead.");
                continue;
            }

            try
            {
                byte[] bytes = await http.GetByteArrayAsync(attachment.Url);

                // The bot re-posts files publicly (forum post, approval message), so redact text files before that too.
                // Only rewrite files that had something redacted, so other files are copied byte for byte.
                if (IssueText.IsTextFile(attachment.Filename, attachment.ContentType)
                    && Encoding.UTF8.GetString(bytes) is string text
                    && IssueText.Redact(text) is string redacted && redacted != text)
                {
                    bytes = Encoding.UTF8.GetBytes(redacted);
                }

                files.Add(new DownloadedFile(attachment.Filename, attachment.ContentType, bytes, attachment.Url));
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                _logger.LogWarning("Issues: couldn't download attachment {File}: {Error}", attachment.Filename, ex.Message);
                notes.Add($"`{attachment.Filename}` couldn't be read, so it wasn't included.");
            }
        }

        return files;
    }

    private async Task<List<DownloadedFile>> DownloadApprovalFilesAsync(Submission submission)
    {
        if (submission.ApprovalMessageId is not ulong messageId
            || _settings.Current.IssueApprovalChannelId is not ulong channelId
            || _forum.Guild?.GetTextChannel(channelId) is not { } channel
            || await channel.GetMessageAsync(messageId) is not { } message)
        {
            return [];
        }

        return await DownloadAsync(message.Attachments, []);
    }

    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..(max - 1)] + "…";
}
