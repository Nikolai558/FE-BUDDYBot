namespace FEBuddyDiscordBot.Issues;

public sealed record ForumTagDefinition(string Name, string? Emoji = null);

/// <summary>
/// The issue forum's tags and how GitHub labels map onto them. GitHub is the source of truth: the bot sets a post's
/// tags from its issue's labels and state, and only the bot applies them (they're moderated tags).
/// </summary>
public static class IssueTags
{
    public const string Bug = "Bug";
    public const string Feature = "Feature";
    public const string Docs = "Docs";
    public const string DevTask = "Dev task";
    public const string NeedsTriage = "Needs triage";
    public const string Fixed = "Fixed";
    public const string WontFix = "Won't fix";
    public const string Duplicate = "Duplicate";
    public const string Invalid = "Invalid";

    /// <summary>Discord allows at most 5 tags on a post.</summary>
    public const int MaxPerPost = 5;

    /// <summary>Every tag the forum needs, in the order they're listed in Discord. Discord allows 20 per forum.</summary>
    public static IReadOnlyList<ForumTagDefinition> All { get; } =
    [
        new(Bug, "🐛"),
        new(Feature, "✨"),
        new(Docs, "📄"),
        new(DevTask, "🔧"),
        new(NeedsTriage, "❔"),
        new("Priority"),
        new("In progress"),
        new("Partially done"),
        new("Planned"),
        new("Needs research"),
        new("Question"),
        new("Help wanted"),
        new(Fixed, "✅"),
        new(WontFix),
        new(Duplicate),
        new(Invalid),
        new("v3.x"),
        new("v2.x"),
    ];

    private static readonly (string Label, string Tag)[] TypeLabels =
    [
        ("bug", Bug),
        ("feature", Feature),
        ("docs", Docs),
        ("task", DevTask),
    ];

    /// <summary>Status labels in priority order: when there are more than 5 tags, earlier ones win.</summary>
    private static readonly (string Label, string Tag)[] StatusLabels =
    [
        ("priority", "Priority"),
        ("in progress", "In progress"),
        ("partially done", "Partially done"),
        ("planned", "Planned"),
        ("needs research", "Needs research"),
        ("question", "Question"),
        ("help wanted", "Help wanted"),
    ];

    private static readonly (string Label, string Tag)[] VersionLabels =
    [
        ("v3.x", "v3.x"),
        ("v2.x", "v2.x"),
    ];

    /// <summary>
    /// The tag names a post should have, most important first, at most <see cref="MaxPerPost"/>:
    /// type, then (if closed) why it was closed, then status, then version.
    /// </summary>
    public static IReadOnlyList<string> ForIssue(IEnumerable<string> labels, string title, string? body, bool isOpen, string? stateReason)
    {
        HashSet<string> labelSet = new(labels, StringComparer.OrdinalIgnoreCase);
        List<string> tags = [];

        tags.AddRange(TypeLabels.Where(t => labelSet.Contains(t.Label)).Select(t => t.Tag));
        if (tags.Count == 0) tags.Add(LooksLikeDevTask(title, body) ? DevTask : NeedsTriage);

        if (!isOpen) tags.Add(ClosedTag(labelSet, stateReason));

        tags.AddRange(StatusLabels.Where(t => labelSet.Contains(t.Label)).Select(t => t.Tag));
        tags.AddRange(VersionLabels.Where(t => labelSet.Contains(t.Label)).Select(t => t.Tag));

        return tags.Distinct().Take(MaxPerPost).ToList();
    }

    /// <summary>
    /// Backup for dev tasks missing the "task" label: the template's "[TASK]" title prefix or its "Kind of work" section.
    /// </summary>
    public static bool LooksLikeDevTask(string title, string? body) =>
        title.TrimStart().StartsWith("[TASK]", StringComparison.OrdinalIgnoreCase)
        || (body?.Contains("### Kind of work", StringComparison.OrdinalIgnoreCase) ?? false);

    private static string ClosedTag(HashSet<string> labels, string? stateReason)
    {
        if (stateReason == "duplicate" || labels.Contains("duplicate")) return Duplicate;
        if (labels.Contains("invalid")) return Invalid;
        if (stateReason == "not_planned" || labels.Contains("won't fix")) return WontFix;
        return Fixed;
    }
}
