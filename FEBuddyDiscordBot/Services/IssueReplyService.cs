using System.Collections.Concurrent;
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
        _discord.MessagesBulkDeleted += OnMessagesBulkDeletedAsync;
    }

    /// <summary>True when every reply is copied: the setting is on and the bot can read message text.</summary>
    public bool MirrorsAllReplies => _settings.Current.IssueReplyMode == IssueReplyMode.MirrorAll && _options.MessageContentIntent;

    /// <summary>The issue a channel is the forum post of, or null if it isn't an issue post.</summary>
    public int? IssueFor(ulong? channelId) =>
        channelId is ulong id
        && _store.GetIssueForThread(id) is int issue
        && _forum.Forum is { } forum && _store.HasPostIn(issue, forum.Id)
            ? issue
            : null;

    // ---- Discord events. They arrive on the gateway thread, so the GitHub work runs in the background. ----

    private Task OnMessageReceivedAsync(SocketMessage message)
    {
        if (MirrorsAllReplies && IsReply(message) && IssueFor(message.Channel.Id) is int issue)
        {
            _ = Task.Run(() => TryAsync(() => SendAsync((IUserMessage)message, message.Channel.Id, issue), message));
        }

        return Task.CompletedTask;
    }

    private Task OnMessageUpdatedAsync(Cacheable<IMessage, ulong> before, SocketMessage after, ISocketMessageChannel channel)
    {
        // Without the Message Content intent an edit arrives with no text, so edits can't be followed.
        // Embeds loading for a link also counts as an update; only a real edit changes the text.
        if (_options.MessageContentIntent && IsReply(after) && after.EditedTimestamp is not null && IssueFor(channel.Id) is int issue)
        {
            _ = Task.Run(() => TryAsync(() => UpdateAsync((IUserMessage)after, channel.Id, issue), after));
        }

        return Task.CompletedTask;
    }

    private Task OnMessageDeletedAsync(Cacheable<IMessage, ulong> message, Cacheable<IMessageChannel, ulong> channel)
    {
        if (IssueFor(channel.Id) is not null) _ = Task.Run(() => DeleteAsync(message.Id));
        return Task.CompletedTask;
    }

    // Moderators purging messages arrive as one bulk event, not one delete per message.
    private Task OnMessagesBulkDeletedAsync(IReadOnlyCollection<Cacheable<IMessage, ulong>> messages, Cacheable<IMessageChannel, ulong> channel)
    {
        if (IssueFor(channel.Id) is not null)
        {
            _ = Task.Run(async () =>
            {
                foreach (Cacheable<IMessage, ulong> message in messages) await DeleteAsync(message.Id);
            });
        }

        return Task.CompletedTask;
    }

    /// <summary>A member's message (not the bot's, a webhook's, or a system message like "pinned a message").</summary>
    private static bool IsReply(IMessage message) =>
        message is IUserMessage { Type: MessageType.Default or MessageType.Reply } && !message.Author.IsBot && !message.Author.IsWebhook
        && message.Id != message.Channel.Id;

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
    // Copying, editing and deleting one message never overlap: each holds that message's lock. Otherwise a delete
    // (or edit) arriving while the copy is still being made would find nothing to delete, and the comment would stay.

    private readonly SemaphoreSlim[] _messageLocks = Enumerable.Range(0, 64).Select(_ => new SemaphoreSlim(1, 1)).ToArray();

    /// <summary>Messages deleted before (or while) they were copied, so a late copy doesn't publish them.</summary>
    private readonly ConcurrentDictionary<ulong, DateTimeOffset> _deleted = new();

    private async Task<T> WithMessageLockAsync<T>(ulong messageId, Func<Task<T>> work)
    {
        SemaphoreSlim gate = _messageLocks[messageId % (ulong)_messageLocks.Length];
        await gate.WaitAsync();
        try
        {
            return await work();
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>
    /// Copy a message to its issue as a GitHub comment. Returns the comment's URL, or null if it was already copied
    /// (or has been deleted).
    /// </summary>
    public Task<string?> SendAsync(IUserMessage message, ulong threadId, int issueNumber) => WithMessageLockAsync(message.Id, async () =>
    {
        if (_deleted.ContainsKey(message.Id) || await _store.GetReplyAsync(message.Id) is not null) return null;

        GitHubComment comment = await _github.CreateCommentOnceAsync(issueNumber, await BuildCommentAsync(message, threadId), MarkerFor(message.Id));
        await _store.SaveReplyAsync(message.Id, issueNumber, comment.Id);
        _logger.LogInformation("Issues: copied {User}'s message {MessageId} to GitHub comment {CommentId} on #{Number}",
            message.Author.Username, message.Id, comment.Id, issueNumber);
        return (string?)comment.HtmlUrl;
    });

    private Task UpdateAsync(IUserMessage message, ulong threadId, int issueNumber) => WithMessageLockAsync(message.Id, async () =>
    {
        if (await _store.GetReplyAsync(message.Id) is not { } reply)
        {
            // Edited before its copy was made: copy it now, with the edited text (the pending copy then skips it).
            if (MirrorsAllReplies && !_deleted.ContainsKey(message.Id))
            {
                GitHubComment comment = await _github.CreateCommentOnceAsync(issueNumber, await BuildCommentAsync(message, threadId), MarkerFor(message.Id));
                await _store.SaveReplyAsync(message.Id, issueNumber, comment.Id);
            }

            return 0;
        }

        if (!await _github.UpdateCommentAsync(reply.CommentId, await BuildCommentAsync(message, threadId)))
        {
            // Deleted on GitHub: stop following it.
            await _store.DeleteReplyAsync(message.Id);
            return 0;
        }

        _logger.LogInformation("Issues: updated GitHub comment {CommentId} on #{Number} after an edit", reply.CommentId, reply.IssueNumber);
        return 0;
    });

    private Task DeleteAsync(ulong messageId) => WithMessageLockAsync(messageId, async () =>
    {
        _deleted[messageId] = DateTimeOffset.UtcNow;
        foreach ((ulong id, DateTimeOffset when) in _deleted)
        {
            if (DateTimeOffset.UtcNow - when > TimeSpan.FromHours(1)) _deleted.TryRemove(id, out _);
        }

        try
        {
            if (await _store.GetReplyAsync(messageId) is not { } reply) return 0;
            await _github.DeleteCommentAsync(reply.CommentId);
            await _store.DeleteReplyAsync(messageId);
            _logger.LogInformation("Issues: deleted GitHub comment {CommentId} on #{Number} (its Discord message was deleted)", reply.CommentId, reply.IssueNumber);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Issues: couldn't delete the GitHub comment for deleted message {MessageId}: {Error}", messageId, ex.Message);
        }

        return 0;
    });

    private async Task<string> BuildCommentAsync(IUserMessage message, ulong threadId)
    {
        SocketGuild? guild = _forum.Guild;

        ReplyAuthor author = new(
            (message.Author as IGuildUser)?.DisplayName ?? guild?.GetUser(message.Author.Id)?.DisplayName ?? message.Author.GlobalName ?? message.Author.Username,
            message.Author.Username,
            await _store.GetGitHubLoginAsync(message.Author.Id));

        string Convert(string text) => DiscordReply.ToGitHubMarkdown(
            text,
            id => guild?.GetUser(id)?.DisplayName,
            id => guild?.GetRole(id)?.Name,
            id => guild?.GetChannel(id) is { } channel && IsPublic(channel) ? channel.Name : null);

        ReplyQuote? quote = null;
        IEmbed? embed = message.ReferencedMessage?.Embeds.FirstOrDefault();

        // Replies to the bot's copies of GitHub comments quote the GitHub commenter. Replies to the bot's own
        // messages (the post's first message, "Closed on GitHub", ...) aren't quoted: the comment is on that issue anyway.
        if (message.ReferencedMessage is { } replied && !(replied.Author.Id == _discord.CurrentUser.Id && embed?.Author is null))
        {
            string name = replied.Author.Id == _discord.CurrentUser.Id
                ? embed!.Author!.Value.Name
                : (replied.Author as IGuildUser)?.DisplayName ?? replied.Author.GlobalName ?? replied.Author.Username;
            string text = string.IsNullOrWhiteSpace(replied.Content) ? embed?.Description ?? "" : replied.Content;

            // The quoted message wasn't necessarily meant for GitHub, so it gets the same cleaning as the reply.
            quote = new ReplyQuote(name, Convert(text));
        }

        string url = $"https://discord.com/channels/{guild?.Id}/{threadId}/{message.Id}";
        return DiscordReply.BuildComment(author, Convert(message.Content), url, await ReadAttachmentsAsync(message), quote) + MarkerFor(message.Id) + "\n";
    }

    /// <summary>The hidden tag in a copied message's comment, to find it if GitHub's answer is lost.</summary>
    private static string MarkerFor(ulong messageId) => IssueText.Marker($"message-{messageId}");

    /// <summary>
    /// Whether @everyone can see a channel, so its name can go on GitHub. Private channels and threads just show
    /// as "#unknown-channel".
    /// </summary>
    private static bool IsPublic(SocketGuildChannel channel)
    {
        if (channel is SocketThreadChannel thread) return thread.Type != ThreadType.PrivateThread && IsPublic(thread.ParentChannel);

        SocketRole everyone = channel.Guild.EveryoneRole;
        return channel.GetPermissionOverwrite(everyone)?.ViewChannel switch
        {
            PermValue.Deny => false,
            PermValue.Allow => true,
            _ => everyone.Permissions.ViewChannel,
        };
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
