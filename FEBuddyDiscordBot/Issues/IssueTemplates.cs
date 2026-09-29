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
/// <param name="Page">Which modal it's asked in: 1 (with the title, before the duplicate check) or 2.</param>
/// <param name="ModalLabel">Label shown in Discord (max 45 characters). Defaults to <paramref name="Heading"/>.</param>
/// <param name="Description">Help text under the label (max 100 characters).</param>
/// <param name="Options">Choices for Select, MultiSelect, Radio and Confirm.</param>
/// <param name="LiveReleases">Fill the choices with FE-BUDDY's latest GitHub releases.</param>
public sealed record IssueField(
    string Id,
    string Heading,
    int Page,
    FieldInput Input,
    bool Required = false,
    string? ModalLabel = null,
    string? Description = null,
    string? Placeholder = null,
    IReadOnlyList<string>? Options = null,
    bool LiveReleases = false)
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
            new("version", "FE-BUDDY version", 1, FieldInput.Select, Required: true,
                Description: "Shown in the version chip at the top of the FE-BUDDY window.", LiveReleases: true),
            new("windows", "Windows version", 1, FieldInput.Radio, Required: true,
                Options: ["Windows 11", "Windows 10", "Other"]),
            new("area", "Where in FE-BUDDY?", 1, FieldInput.MultiSelect, Required: true,
                Description: "Pick every part of the app involved.",
                Options:
                [
                    "Dashboard",
                    "AIRAC Service - General / running the service",
                    "AIRAC Service - Airports",
                    "AIRAC Service - Airways",
                    "AIRAC Service - Arrivals",
                    "AIRAC Service - ARTCC Boundaries",
                    "AIRAC Service - Departures",
                    "AIRAC Service - Fixes",
                    "AIRAC Service - NAVAIDs",
                    "AIRAC Service - Procedures",
                    "AIRAC Service - Telephony",
                    "AIRAC Service - Wx Stations",
                    "AIRAC Service - vNAS Alias Upload",
                    "AIRAC Service - File Names",
                    "File Conversions - DAT to GeoJSON",
                    "File Conversions - SCT2 to GeoJSON",
                    "File Conversions - ERAM to GeoJSON",
                    "Map",
                    "Settings",
                    "Updating or installing",
                    "Other / not sure",
                ]),
            new("airac", "AIRAC cycle and facility", 1, FieldInput.Short,
                Description: "If it happened while working with AIRAC data: which cycle and which ARTCC?",
                Placeholder: "e.g. 2610, ZLC"),
            new("what-happened", "What happened?", 2, FieldInput.Paragraph, Required: true,
                Description: "Include the exact text of any error message."),
            new("steps", "Steps to reproduce", 2, FieldInput.Paragraph, Required: true,
                Placeholder: "1. Open AIRAC Service > Airways\n2. Turn on ...\n3. Click Run AIRAC Service"),
            new("expected", "What did you expect to happen?", 2, FieldInput.Paragraph, Required: true),
            new("attachments", "Log, screenshots, and files", 2, FieldInput.Files,
                Description: @"Log: %APPDATA%\FE-Buddy\Logs\FE-Buddy_<date>.log. Screenshots and input files help too."),
            new("checks", "Before you submit", 2, FieldInput.Confirm, Required: true,
                ModalLabel: "Nothing private attached",
                Options: ["Nothing I've attached contains a GitHub token, password, or other private information."]),
        ]),

        new(IssueKind.Feature, "Feature request", "✨", "[FEAT REQ.] - ", ["feature", "v3.x"],
        [
            new("problem", "What problem would this solve?", 1, FieldInput.Paragraph, Required: true,
                Description: "What are you trying to do, and what makes it hard or slow today?"),
            new("solution", "What would you like FE-BUDDY to do?", 2, FieldInput.Paragraph, Required: true,
                ModalLabel: "What should FE-BUDDY do?",
                Description: "Describe how it would work. Mock-ups or example output help a lot."),
            new("area", "Which part of FE-BUDDY?", 1, FieldInput.MultiSelect,
                Options: ["Dashboard", "AIRAC Service (any sub-service)", "File Conversions", "Map", "Settings", "Updating or installing", "Something new", "Not sure"]),
            new("who", "Who would use it?", 2, FieldInput.Short,
                Description: "Your role and facility help us understand how widely it applies.",
                Placeholder: "e.g. Facility engineer at ZLC, for CRC video maps"),
            new("alternatives", "How do you handle it today?", 2, FieldInput.Paragraph,
                Description: "Workarounds, other tools, or other approaches you've considered."),
            new("context", "Anything else?", 2, FieldInput.Paragraph,
                Description: "Links, or FAA or vNAS references."),
            new("attachments", "Screenshots and example files", 2, FieldInput.Files),
        ]),

        new(IssueKind.Docs, "Documentation problem", "📄", "[DOCS] - ", ["docs", "v3.x"],
        [
            new("where", "Where is it?", 1, FieldInput.MultiSelect, Required: true,
                Options:
                [
                    "User Guide",
                    "FAQ and troubleshooting",
                    "Getting started",
                    "GitHub token guide",
                    "Glossary",
                    "Text inside the app (tooltips, messages, descriptions)",
                    "Developer docs",
                    "Other",
                ]),
            new("location", "Link or location", 1, FieldInput.Short,
                Description: "A link to the page and section, or where it appears in the app.",
                Placeholder: "e.g. User Guide > Airways"),
            new("problem", "What's wrong or missing?", 2, FieldInput.Paragraph, Required: true),
            new("suggestion", "Suggested fix", 2, FieldInput.Paragraph,
                Description: "What it should say instead, if you know."),
        ]),

        new(IssueKind.Task, "Development task", "🔧", "[TASK] - ", ["task", "v3.x"],
        [
            new("kind", "Kind of work", 1, FieldInput.MultiSelect, Required: true,
                Options: ["UI (views, cards, styles)", "Logic (FeBuddy.Core)", "Installer or updater", "Build, CI, or release", "Tests", "Cleanup or refactor"]),
            new("description", "What needs to be done, and why?", 1, FieldInput.Paragraph, Required: true,
                ModalLabel: "What needs to be done, and why?"),
            new("steps", "Steps", 2, FieldInput.Paragraph,
                Description: "The pieces of work needed to finish this.", Placeholder: "1.\n2.\n3."),
            new("tests", "Tests needed", 2, FieldInput.Paragraph,
                Description: "For logic changes, the tests needed to keep FeBuddy.Core's coverage up."),
            new("references", "References", 2, FieldInput.Paragraph,
                Description: "Related issues, files, docs, or FAA and vNAS references."),
        ], RestrictedToDevTaskRoles: true),
    ];
}
