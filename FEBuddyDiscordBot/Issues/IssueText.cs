using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace FEBuddyDiscordBot.Issues;

/// <summary>
/// Turns a Discord submission into GitHub issue text, laid out like an issue made from the web form.
/// </summary>
public static partial class IssueText
{
    /// <summary>GitHub rejects issue bodies longer than 65,536 characters.</summary>
    public const int MaxBodyLength = 65_000;

    /// <summary>Text files up to this size are pasted into the issue.</summary>
    public const int MaxInlineBytes = 50_000;

    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "the", "and", "for", "with", "when", "not", "does", "doesn", "don", "can", "cannot", "can't", "into", "from",
        "this", "that", "are", "was", "were", "have", "has", "had", "but", "all", "any", "out", "its", "it's", "you",
        "your", "add", "should", "would", "could", "feb", "buddy", "fe-buddy", "febuddy", "bug", "feature", "request",
    };

    public static string BuildBody(IssueDraft draft, string? postUrl)
    {
        string body = Build(draft, postUrl, includeFileContents: true);
        return body.Length <= MaxBodyLength ? body : Build(draft, postUrl, includeFileContents: false);
    }

    private static string Build(IssueDraft draft, string? postUrl, bool includeFileContents)
    {
        StringBuilder body = new();

        foreach (IssueField field in draft.Template.Fields)
        {
            body.Append("### ").Append(field.Heading).Append("\n\n");

            switch (field.Input)
            {
                case FieldInput.Files:
                    AppendAttachments(body, draft.Attachments, postUrl, includeFileContents);
                    break;
                case FieldInput.Confirm:
                    body.Append("- [X] ").Append(IssueTemplate.SearchedConfirmation).Append('\n');
                    foreach (string option in field.Options ?? []) body.Append("- [X] ").Append(option).Append('\n');
                    break;
                default:
                    body.Append(draft.Values.TryGetValue(field.Id, out string? value) && !string.IsNullOrWhiteSpace(value) ? EscapeMentions(value.Trim()) : "_No response_").Append('\n');
                    break;
            }

            body.Append('\n');
        }

        body.Append("---\n\n<sub>Submitted from the ")
            .Append(postUrl is null ? "FE-BUDDY Discord" : $"[FE-BUDDY Discord]({postUrl})")
            .Append(" by ").Append(Credit(draft)).Append(".</sub>\n");

        return body.ToString();
    }

    public static string Credit(IssueDraft draft) => draft.Credit switch
    {
        CreditStyle.DiscordId => $"Discord user ID `{draft.UserId}`",
        _ => $"Discord user `{draft.UserName}`",
    };

    private static void AppendAttachments(StringBuilder body, IReadOnlyList<DraftAttachment> attachments, string? postUrl, bool includeFileContents)
    {
        if (attachments.Count == 0)
        {
            body.Append("_No response_\n");
            return;
        }

        foreach (DraftAttachment file in attachments)
        {
            body.Append("- 📎 `").Append(file.FileName).Append("` (")
                .Append(file.ContentType ?? "file").Append(", ").Append(FormatSize(file.Size)).Append(") was attached on Discord")
                .Append(postUrl is null ? "." : $": [view it in the post]({postUrl})").Append('\n');
        }

        foreach (DraftAttachment file in attachments.Where(f => f.InlineText is not null))
        {
            body.Append("\n<details><summary>Contents of <code>").Append(file.FileName).Append("</code></summary>\n\n");
            if (includeFileContents)
            {
                string fence = Fence(file.InlineText!);
                body.Append(fence).Append("text\n").Append(file.InlineText!.TrimEnd()).Append('\n').Append(fence).Append('\n');
            }
            else
            {
                body.Append("Too long to include here. Open the file in the Discord post.\n");
            }

            body.Append("\n</details>\n");
        }
    }

    /// <summary>A code fence longer than any run of backticks in the text, so the text can't close it early.</summary>
    private static string Fence(string text)
    {
        int longest = 0, run = 0;
        foreach (char c in text)
        {
            run = c == '`' ? run + 1 : 0;
            longest = Math.Max(longest, run);
        }

        return new string('`', Math.Max(3, longest + 1));
    }

    public static string FormatSize(int bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => string.Create(CultureInfo.InvariantCulture, $"{bytes / 1024.0:0} KB"),
        _ => string.Create(CultureInfo.InvariantCulture, $"{bytes / 1024.0 / 1024.0:0.0} MB"),
    };

    /// <summary>True for files small enough, and textual enough, to paste into the issue.</summary>
    public static bool CanInline(string fileName, string? contentType, int size) =>
        size <= MaxInlineBytes
        && (contentType?.StartsWith("text/", StringComparison.OrdinalIgnoreCase) == true
            || Path.GetExtension(fileName).ToLowerInvariant() is ".log" or ".txt");

    /// <summary>
    /// Stops @name and @org/team in submitted text from notifying GitHub users: the issue is posted by the bot,
    /// so anyone on Discord could otherwise ping any GitHub account. E-mail addresses are left alone.
    /// </summary>
    public static string EscapeMentions(string text) => MentionPattern().Replace(text, "@​");

    [GeneratedRegex(@"(?<![\w.@/`])@(?=[A-Za-z0-9])")]
    private static partial Regex MentionPattern();

    /// <summary>Blanks out anything that looks like a GitHub token.</summary>
    public static string Redact(string text) => TokenPattern().Replace(text, "[REDACTED TOKEN]");

    [GeneratedRegex(@"\b(?:gh[pousr]_[A-Za-z0-9]{30,}|github_pat_[A-Za-z0-9_]{20,})")]
    private static partial Regex TokenPattern();

    /// <summary>
    /// Search terms for the duplicate check: up to five meaningful words from the title, any of which may match.
    /// Null when the title has nothing worth searching for.
    /// </summary>
    public static string? SearchTerms(string title)
    {
        string[] words = WordPattern().Matches(title)
            .Select(m => m.Value)
            .Where(w => w.Length >= 3 && !StopWords.Contains(w))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .ToArray();

        return words.Length == 0 ? null : string.Join(" OR ", words);
    }

    [GeneratedRegex(@"[A-Za-z0-9]+")]
    private static partial Regex WordPattern();

    /// <summary>
    /// An issue body tidied up for a Discord embed: headings become bold, HTML comments, collapsed sections and
    /// the "Submitted from Discord" footer are dropped, and it's cut to <paramref name="maxLength"/> characters.
    /// </summary>
    public static string ForDiscord(string? body, int maxLength = 3500)
    {
        if (string.IsNullOrWhiteSpace(body)) return "_No description._";

        string text = body.Replace("\r\n", "\n");
        text = HtmlCommentPattern().Replace(text, "");
        text = DetailsPattern().Replace(text, "_(collapsed section: open the issue on GitHub)_");
        text = FooterPattern().Replace(text, "");
        text = HeadingPattern().Replace(text, "**$1**");
        text = BlankLinesPattern().Replace(text, "\n\n").Trim();

        if (text.Length <= maxLength) return text;

        const string more = "\n\n… _continued on GitHub_";
        return text[..(maxLength - more.Length)].TrimEnd() + more;
    }

    [GeneratedRegex(@"<!--.*?-->", RegexOptions.Singleline)]
    private static partial Regex HtmlCommentPattern();

    [GeneratedRegex(@"<details>.*?</details>", RegexOptions.Singleline | RegexOptions.IgnoreCase)]
    private static partial Regex DetailsPattern();

    [GeneratedRegex(@"(?:^|\n)---\n+<sub>Submitted from the .*?</sub>\s*$", RegexOptions.Singleline)]
    private static partial Regex FooterPattern();

    [GeneratedRegex(@"^#{1,6}[ \t]+(.+?)[ \t]*#*$", RegexOptions.Multiline)]
    private static partial Regex HeadingPattern();

    [GeneratedRegex(@"\n{3,}")]
    private static partial Regex BlankLinesPattern();
}
