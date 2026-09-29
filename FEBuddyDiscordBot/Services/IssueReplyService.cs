using System.Text;
using FEBuddyDiscordBot.DataAccess;
using FEBuddyDiscordBot.Issues;
using FEBuddyDiscordBot.Models;

namespace FEBuddyDiscordBot.Services;

/// <summary>
/// Copies replies in issue forum posts to their GitHub issue as comments, and keeps the comment in step when the
/// message is edited or deleted. With <see cref="IssueReplyMode.MirrorAll"/> every reply is copied (this needs the
/// Message Content intent); otherwise only messages sent with the "Send to GitHub" message action.
/// </summary>
public sealed class IssueReplyService
{
    private readonly DiscordSocketClient _discord;
    private readonly GuildSettingsStore _settings;
    private readonly IssueStore _store;
    private readonly GitHubApi _github;
    private readonly IssueForumService _forum;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly BotOptions _options;
    private readonly ILogger<IssueReplyService> _logger;

    public IssueReplyService(
        DiscordSocketClient discord,
        GuildSettingsStore settings,
        IssueStore store,
        GitHubApi github,
        IssueForumService forum,
        IHttpClientFactory httpClientFactory,
        IOptions<BotOptions> options,
        ILogger<IssueReplyService> logger)
    {
        _discord = discord;
        _settings = settings;
        _store = store;
        _github = github;
        _forum = forum;
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;

        _discord.MessageReceived += OnMessageReceivedAsync;
        _discord.MessageUpdated += OnMessageUpdatedAsync;
        _discord.MessageDeleted += OnMessageDeletedAsync;
    }

    /// <summary>True when every reply is copied: the setting is on and the bot can read message text.</summary>
    public bool MirrorsAllReplies => _settings.Current.IssueReplyMode == IssueReplyMode.MirrorAll && _options.MessageContentIntent;

    /// <summary>The issue a channel is the forum post of, or null if it isn't an issue post.</summary>
    public int? IssueFor(IChannel? channel) =>
        channel is IThreadChannel thread
        && _store.GetIssueForThread(thread.Id) is int issue
        && _forum.Forum is { } forum && _store.HasPostIn(issue, forum.Id)
            ? issue
            : null;

    // ---- Discord events. They arrive on the gateway thread, so the GitHub work runs in the background. ----

    private Task OnMessageReceivedAsync(SocketMessage message)
    {
        if (MirrorsAllReplies && IsReply(message) && IssueFor(message.Channel) is int issue)
        {
            _ = Task.Run(() => TryAsync(() => SendAsync((IUserMessage)message, issue), message));
        }

        return Task.CompletedTask;
    }

    private Task OnMessageUpdatedAsync(Cacheable<IMessage, ulong> before, SocketMessage after, ISocketMessageChannel channel)
    {
        // Without the Message Content intent an edit arrives with no text, so edits can't be followed.
        // Embeds loading for a link also counts as an update; only a real edit changes the text.
        if (_options.MessageContentIntent && IsReply(after) && after.EditedTimestamp is not null && IssueFor(after.Channel) is not null)
        {
            _ = Task.Run(() => TryAsync(() => UpdateAsync((IUserMessage)after), after));
        }

        return Task.CompletedTask;
    }

    private Task OnMessageDeletedAsync(Cacheable<IMessage, ulong> message, Cacheable<IMessageChannel, ulong> channel)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                if (await _store.GetReplyAsync(message.Id) is not { } reply) return;
                await _github.DeleteCommentAsync(reply.CommentId);
                await _store.DeleteReplyAsync(message.Id);
                _logger.LogInformation("Issues: deleted GitHub comment {CommentId} on #{Number} (its Discord message was deleted)", reply.CommentId, reply.IssueNumber);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Issues: couldn't delete the GitHub comment for deleted message {MessageId}: {Error}", message.Id, ex.Message);
            }
        });

        return Task.CompletedTask;
    }

    /// <summary>A member's message (not the bot's, a webhook's, or a system message like "pinned a message").</summary>
    private static bool IsReply(IMessage message) =>
        message is IUserMessage { Type: MessageType.Default or MessageType.Reply } && !message.Author.IsBot && !message.Author.IsWebhook
        && message.Channel is IThreadChannel thread && message.Id != thread.Id;

    private async Task TryAsync(Func<Task> work, IMessage message)
    {
        try
        {
            await work();
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Issues: couldn't copy message {MessageId} to GitHub: {Error}", message.Id, ex is GitHubException ? ex.Message : ex.ToString());
            try
            {
                // Let the member know this one didn't reach GitHub.
                await ((IUserMessage)message).AddReactionAsync(new Emoji("⚠️"));
            }
            catch (Exception reactionError)
            {
                _logger.LogDebug(reactionError, "Issues: couldn't add the warning reaction");
            }
        }
    }

    // ---- Copying ----

    /// <summary>
    /// Copy a message to its issue as a GitHub comment. Returns the comment's URL, or null if it was already copied.
    /// </summary>
    public async Task<string?> SendAsync(IUserMessage message, int issueNumber)
    {
        if (await _store.GetReplyAsync(message.Id) is not null) return null;

        GitHubComment comment = await _github.CreateCommentAsync(issueNumber, await BuildCommentAsync(message));
        await _store.SaveReplyAsync(message.Id, issueNumber, comment.Id);
        _logger.LogInformation("Issues: copied {User}'s message {MessageId} to GitHub comment {CommentId} on #{Number}",
            message.Author.Username, message.Id, comment.Id, issueNumber);
        return comment.HtmlUrl;
    }

    private async Task UpdateAsync(IUserMessage message)
    {
        if (await _store.GetReplyAsync(message.Id) is not { } reply) return;

        if (!await _github.UpdateCommentAsync(reply.CommentId, await BuildCommentAsync(message)))
        {
            // Deleted on GitHub: stop following it.
            await _store.DeleteReplyAsync(message.Id);
            return;
        }

        _logger.LogInformation("Issues: updated GitHub comment {CommentId} on #{Number} after an edit", reply.CommentId, reply.IssueNumber);
    }

    private async Task<string> BuildCommentAsync(IUserMessage message)
    {
        SocketGuild? guild = _forum.Guild;

        ReplyAuthor author = new(
            (message.Author as IGuildUser)?.DisplayName ?? guild?.GetUser(message.Author.Id)?.DisplayName ?? message.Author.GlobalName ?? message.Author.Username,
            message.Author.Username,
            await _store.GetGitHubLoginAsync(message.Author.Id));

        string markdown = DiscordReply.ToGitHubMarkdown(
            message.Content,
            id => guild?.GetUser(id)?.DisplayName,
            id => guild?.GetRole(id)?.Name,
            id => guild?.GetChannel(id)?.Name);

        ReplyQuote? quote = message.ReferencedMessage is { } replied
            ? new ReplyQuote(
                (replied.Author as IGuildUser)?.DisplayName ?? replied.Author.GlobalName ?? replied.Author.Username,
                string.IsNullOrWhiteSpace(replied.Content) ? replied.Embeds.FirstOrDefault()?.Description ?? "" : replied.Content)
            : null;

        return DiscordReply.BuildComment(author, markdown, message.GetJumpUrl(), await ReadAttachmentsAsync(message), quote);
    }

    /// <summary>The message's files, with small text files read (and redacted) so they can be pasted into the comment.</summary>
    private async Task<List<DraftAttachment>> ReadAttachmentsAsync(IMessage message)
    {
        HttpClient http = _httpClientFactory.CreateClient(IssueSubmissionService.FilesHttpClientName);
        List<DraftAttachment> files = [];

        foreach (IAttachment file in message.Attachments)
        {
            string? text = null;
            if (IssueText.CanInline(file.Filename, file.ContentType, file.Size))
            {
                try
                {
                    text = IssueText.Redact(Encoding.UTF8.GetString(await http.GetByteArrayAsync(file.Url)));
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
                {
                    _logger.LogDebug("Issues: couldn't read attachment {File}: {Error}", file.Filename, ex.Message);
                }
            }

            files.Add(new DraftAttachment(file.Filename, file.ContentType, file.Size, file.Url, text));
        }

        return files;
    }
}
