using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace FEBuddyDiscordBot.Issues;

/// <summary>Who wrote a Discord reply. <see cref="GitHubLogin"/> is set when they've linked a GitHub account.</summary>
public sealed record ReplyAuthor(string DisplayName, string UserName, string? GitHubLogin = null);

/// <summary>The message a Discord reply answers, shown as a quote above it on GitHub.</summary>
public sealed record ReplyQuote(string AuthorName, string Text);

/// <summary>
/// Turns a Discord message in an issue's forum post into a GitHub comment.
/// </summary>
public static partial class DiscordReply
{
    /// <summary>
    /// Discord message text as GitHub markdown: user, role and channel mentions become plain names, custom emoji
    /// become :name:, timestamps become UTC times, GitHub tokens are redacted and @names can't ping GitHub users.
    /// </summary>
    public static string ToGitHubMarkdown(string content, Func<ulong, string?> userName, Func<ulong, string?> roleName, Func<ulong, string?> channelName)
    {
        string text = UserMention().Replace(content, m => "@" + (userName(ulong.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)) ?? "unknown-user"));
        text = RoleMention().Replace(text, m => "@" + (roleName(ulong.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)) ?? "unknown-role"));
        text = ChannelMention().Replace(text, m => "#" + (channelName(ulong.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)) ?? "unknown-channel"));
        text = CustomEmoji().Replace(text, ":$1:");
        text = Timestamp().Replace(text, m =>
            DateTimeOffset.FromUnixTimeSeconds(long.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture)).UtcDateTime
                .ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture));

        return IssueText.EscapeMentions(IssueText.Redact(text));
    }

    /// <summary>
    /// The GitHub comment for a Discord message: who wrote it with a link back to the message, the message it
    /// replies to (if any), its text, and its files (text files pasted in as far as GitHub's length limit allows).
    /// </summary>
    public static string BuildComment(ReplyAuthor author, string markdown, string messageUrl, IReadOnlyList<DraftAttachment> attachments, ReplyQuote? quote = null) =>
        IssueText.FitFiles(pasted => Build(author, markdown, messageUrl, attachments, quote, pasted), attachments.Count(a => a.InlineText is not null));

    private static string Build(ReplyAuthor author, string markdown, string messageUrl, IReadOnlyList<DraftAttachment> attachments, ReplyQuote? quote, int filesToPaste)
    {
        StringBuilder body = new();

        body.Append("**").Append(EscapeName(author.DisplayName)).Append("** (")
            .Append(author.GitHubLogin is string login ? $"[{login}](https://github.com/{login})" : $"Discord user `{author.UserName}`")
            .Append(") [on Discord](").Append(messageUrl).Append("):\n\n");

        if (quote is not null)
        {
            string snippet = quote.Text.ReplaceLineEndings(" ").Trim();
            if (snippet.Length > 150) snippet = snippet[..149] + "…";
            body.Append("> Replying to **").Append(EscapeName(quote.AuthorName)).Append("**: ").Append(snippet).Append("\n\n");
        }

        if (!string.IsNullOrWhiteSpace(markdown)) body.Append(markdown.Trim()).Append('\n');

        if (attachments.Count > 0)
        {
            body.Append('\n');
            IssueText.AppendAttachments(body, attachments, messageUrl, "view it on Discord", filesToPaste);
        }

        return body.ToString();
    }

    /// <summary>A display name that can't break the bold around it or ping anyone on GitHub.</summary>
    private static string EscapeName(string name) => IssueText.EscapeMentions(MarkdownChars().Replace(name, @"\$0"));

    [GeneratedRegex(@"<@!?(\d+)>")]
    private static partial Regex UserMention();

    [GeneratedRegex(@"<@&(\d+)>")]
    private static partial Regex RoleMention();

    [GeneratedRegex(@"<#(\d+)>")]
    private static partial Regex ChannelMention();

    [GeneratedRegex(@"<a?:(\w+):\d+>")]
    private static partial Regex CustomEmoji();

    [GeneratedRegex(@"<t:(\d+)(?::[tTdDfFR])?>")]
    private static partial Regex Timestamp();

    [GeneratedRegex(@"[\\`*_~\[\]<>|]")]
    private static partial Regex MarkdownChars();
}
