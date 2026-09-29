using System.Text.Json.Serialization;

namespace FEBuddyDiscordBot.Issues;

/// <summary>How the reporter is named in the GitHub issue.</summary>
public enum CreditStyle
{
    DiscordName,
    DiscordId,

    /// <summary>Their GitHub account, linked with /link-github. GitHub notifies them and subscribes them to the issue.</summary>
    GitHub,
}

/// <summary>
/// A file attached to a submission. <see cref="InlineText"/> holds the (redacted) contents of small text files
/// so they can be pasted into the issue.
/// </summary>
public sealed record DraftAttachment(string FileName, string? ContentType, int Size, string Url, string? InlineText = null);

/// <summary>
/// An issue being filled in on Discord. Kept in memory between the two modals, and saved as JSON while it waits for approval.
/// </summary>
public sealed class IssueDraft
{
    public IssueKind Kind { get; set; }
    public ulong UserId { get; set; }
    public string UserName { get; set; } = "";
    public string Title { get; set; } = "";

    /// <summary>Answers by field id, already formatted as they appear in the issue body.</summary>
    public Dictionary<string, string> Values { get; set; } = [];

    public List<DraftAttachment> Attachments { get; set; } = [];
    public CreditStyle Credit { get; set; } = CreditStyle.DiscordName;

    /// <summary>The reporter's linked GitHub account, if any.</summary>
    public string? GitHubLogin { get; set; }
    public DateTimeOffset StartedUtc { get; set; } = DateTimeOffset.UtcNow;

    [JsonIgnore]
    public IssueTemplate Template => IssueTemplate.For(Kind);

    /// <summary>The GitHub issue title, e.g. "[BUG] - Airways crash".</summary>
    [JsonIgnore]
    public string FullTitle => Template.TitlePrefix + Title;
}
