namespace FEBuddyDiscordBot.Issues;

public enum IssueKind
{
    [ChoiceDisplay("Bug report")]
    Bug,

    [ChoiceDisplay("Feature request")]
    Feature,

    [ChoiceDisplay("Documentation problem")]
    Docs,

    [ChoiceDisplay("Development task")]
    Task,
}

public enum FieldInput
{
    /// <summary>One-line text.</summary>
    Short,

    /// <summary>Multi-line text.</summary>
    Paragraph,

    /// <summary>Dropdown, one choice.</summary>
    Select,

    /// <summary>Dropdown, several choices.</summary>
    MultiSelect,

    /// <summary>Radio buttons, one choice.</summary>
    Radio,

    /// <summary>File upload.</summary>
    Files,

    /// <summary>Checkboxes that must all be ticked.</summary>
    Confirm,
}

/// <summary>
/// One question from a GitHub issue template, as asked in a Discord modal.
/// </summary>
/// <param name="Id">The template field's id; also the modal component's custom id.</param>
/// <param name="Heading">The field's label in the GitHub template; becomes the "### Heading" in the issue body.</param>
/// <param name="Page">Which modal it's asked in: 1 (with the title, before the duplicate check) or 2.
/// Each template asks its main question on page 1, and needs at least one question on page 2.</param>
/// <param name="ModalLabel">Label shown in Discord (max 45 characters). Defaults to <paramref name="Heading"/>.</param>
/// <param name="Description">Help text under the label (max 100 characters).</param>
/// <param name="Options">Choices for Select, MultiSelect, Radio and Confirm.</param>
public sealed record IssueField(
    string Id,
    string Heading,
    int Page,
    FieldInput Input,
    bool Required = false,
    string? ModalLabel = null,
    string? Description = null,
    string? Placeholder = null,
    IReadOnlyList<string>? Options = null)
{
    public string Label => ModalLabel ?? Heading;
}

/// <summary>
/// A GitHub issue template (FE-BUDDY's .github/ISSUE_TEMPLATE) rebuilt as two Discord modals.
/// Keep these in step with the YAML templates: same title prefix, labels, headings and choices.
/// </summary>
/// <param name="Fields">In the template's order, which is the order of the headings in the issue body.</param>
/// <param name="RestrictedToDevTaskRoles">Only members with a dev-task role (or Manage Server) may submit it.</param>
public sealed record IssueTemplate(
    IssueKind Kind,
    string Name,
    string Emoji,
    string TitlePrefix,
    IReadOnlyList<string> Labels,
    IReadOnlyList<IssueField> Fields,
    bool RestrictedToDevTaskRoles = false)
{
    /// <summary>The Confirm field's options are rendered as ticked boxes; the duplicate check covers this one.</summary>
    public const string SearchedConfirmation = "I searched the open issues and this hasn't been reported yet.";

    public IEnumerable<IssueField> Page(int page) => Fields.Where(f => f.Page == page);

    public static IssueTemplate For(IssueKind kind) => All.Single(t => t.Kind == kind);

    public static IReadOnlyList<IssueTemplate> All { get; } =
    [
        new(IssueKind.Bug, "Bug report", "🐛", "[BUG] - ", ["bug", "v3.x"],
        [
            new("what-happened", "What went wrong?", 1, FieldInput.Paragraph, Required: true,
                Description: "What happened, and what you expected instead. Include the exact text of any error.",
                Placeholder: "e.g. I ran the AIRAC Service for ZLC with Airways on. V21 is missing from the airway file."),
            new("steps", "Steps to reproduce (optional)", 2, FieldInput.Paragraph,
                ModalLabel: "Steps to reproduce",
                Description: "If you remember what you clicked, list it here so we can make it happen too.",
                Placeholder: "1. Open AIRAC Service > Airways\n2. Turn on ...\n3. Click Run AIRAC Service"),
            new("attachments", "Log, screenshots, and files (optional)", 2, FieldInput.Files,
                ModalLabel: "Log, screenshots, and files",
                Description: @"Log: %APPDATA%\FE-Buddy\Logs\FE-Buddy_<date>.log. Crash on launch: also febuddy-wpf-crash.txt."),
            new("checks", "Before you submit", 2, FieldInput.Confirm, Required: true,
                ModalLabel: "Nothing private attached",
                Options: ["Nothing I've attached contains a GitHub token, password, or other private information."]),
        ]),

        new(IssueKind.Feature, "Feature request", "✨", "[FEAT REQ.] - ", ["feature", "v3.x"],
        [
            new("request", "What would you like FE-BUDDY to do?", 1, FieldInput.Paragraph, Required: true,
                Description: "Plain words are perfect. If you can, say what it would fix or save you today.",
                Placeholder: "e.g. Let me pick which airways the AIRAC Service leaves out of the airway files."),
            new("attachments", "Examples or attachments (optional)", 2, FieldInput.Files,
                ModalLabel: "Examples or attachments",
                Description: "Screenshots, sketches, or example files. Links can go in the forum post."),
        ]),

        new(IssueKind.Docs, "Documentation problem", "📄", "[DOCS] - ", ["docs", "v3.x"],
        [
            new("problem", "What's wrong or missing, and where?", 1, FieldInput.Paragraph, Required: true,
                Description: "A link to the page, or where you saw it in the app, helps us find it.",
                Placeholder: "e.g. User Guide > Airways says the output goes to the Airways folder, but it goes to ..."),
            new("suggestion", "Suggested fix (optional)", 2, FieldInput.Paragraph,
                ModalLabel: "Suggested fix",
                Description: "What it should say instead, if you know.",
                Placeholder: "e.g. Change it to \"Output is saved to ...\""),
        ]),

        new(IssueKind.Task, "Development task", "🔧", "[TASK] - ", ["task", "v3.x"],
        [
            new("kind", "Kind of work", 2, FieldInput.MultiSelect, Required: true,
                Options: ["UI (views, cards, styles)", "Logic (FeBuddy.Core)", "Installer or updater", "Build, CI, or release", "Tests", "Cleanup or refactor"]),
            new("description", "What needs to be done, and why?", 1, FieldInput.Paragraph, Required: true,
                Description: "What to add, rework, or remove, and why. Steps, tests needed and references help.",
                Placeholder: "Steps:\n1.\n2.\n\nTests needed:\n\nReferences:"),
        ], RestrictedToDevTaskRoles: true),
    ];
}
