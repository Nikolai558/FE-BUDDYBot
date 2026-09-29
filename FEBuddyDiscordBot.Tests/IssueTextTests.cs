using FEBuddyDiscordBot.Issues;

namespace FEBuddyDiscordBot.Tests;

public sealed class IssueTextTests
{
    private static IssueDraft BugDraft() => new()
    {
        Kind = IssueKind.Bug,
        UserId = 123456789012345678,
        UserName = "some_user",
        Title = "Airways crash",
        Values =
        {
            ["version"] = "3.0.0-alpha.2",
            ["windows"] = "Windows 11",
            ["area"] = "AIRAC Service - Airways, Map",
            ["airac"] = "",
            ["what-happened"] = "It crashed.",
            ["steps"] = "1. Run it",
            ["expected"] = "No crash",
        },
    };

    [Fact]
    public void Body_follows_the_template_headings_in_order()
    {
        string body = IssueText.BuildBody(BugDraft(), postUrl: null);

        string[] headings = body.Split('\n').Where(l => l.StartsWith("### ")).ToArray();
        Assert.Equal(IssueTemplate.For(IssueKind.Bug).Fields.Select(f => "### " + f.Heading), headings);

        Assert.Contains("### FE-BUDDY version\n\n3.0.0-alpha.2\n", body);
        Assert.Contains("### AIRAC cycle and facility\n\n_No response_\n", body);
        Assert.Contains("### Log, screenshots, and files\n\n_No response_\n", body);
        Assert.Contains("- [X] " + IssueTemplate.SearchedConfirmation, body);
        Assert.Contains("- [X] Nothing I've attached contains", body);
    }

    [Fact]
    public void Footer_credits_the_reporter_and_links_the_post()
    {
        IssueDraft draft = BugDraft();

        Assert.Contains("[FE-BUDDY Discord](https://discord.com/channels/1/2) by Discord user `some_user`.", IssueText.BuildBody(draft, "https://discord.com/channels/1/2"));

        draft.Credit = CreditStyle.DiscordId;
        Assert.Contains("by Discord user ID `123456789012345678`.", IssueText.BuildBody(draft, null));
    }

    [Fact]
    public void Attachments_are_listed_and_text_files_pasted()
    {
        IssueDraft draft = BugDraft();
        draft.Attachments =
        [
            new("screen.png", "image/png", 245_760, "https://cdn/x"),
            new("FE-Buddy.log", "text/plain", 30, "https://cdn/y", "line with ``` fence"),
        ];

        string body = IssueText.BuildBody(draft, "https://discord.com/channels/1/2");

        Assert.Contains("- 📎 `screen.png` (image/png, 240 KB) was attached on Discord: [view it in the post](https://discord.com/channels/1/2)", body);
        Assert.Contains("<details><summary>Contents of <code>FE-Buddy.log</code></summary>", body);
        Assert.Contains("````text\nline with ``` fence\n````", body);
    }

    [Fact]
    public void Too_long_body_drops_pasted_file_contents()
    {
        IssueDraft draft = BugDraft();
        draft.Attachments = [new("big.log", "text/plain", 49_000, "u", new string('x', IssueText.MaxBodyLength))];

        string body = IssueText.BuildBody(draft, null);

        Assert.True(body.Length < IssueText.MaxBodyLength);
        Assert.Contains("Too long to include here", body);
    }

    [Theory]
    [InlineData("ping @octocat and @org/team", "ping @​octocat and @​org/team")]
    [InlineData("mail me at nik@example.com", "mail me at nik@example.com")]
    [InlineData("`@code` stays", "`@code` stays")]
    public void Github_mentions_are_escaped(string text, string expected)
    {
        Assert.Equal(expected, IssueText.EscapeMentions(text));
    }

    [Fact]
    public void Submitted_text_cannot_mention_github_users()
    {
        IssueDraft draft = BugDraft();
        draft.Values["what-happened"] = "@someone broke it";

        Assert.Contains("### What happened?\n\n@​someone broke it\n", IssueText.BuildBody(draft, null));
    }

    [Fact]
    public void Github_tokens_are_redacted()
    {
        string text = "token=ghp_" + new string('a', 36) + " and github_pat_" + new string('B', 40) + " stay_ok";

        Assert.Equal("token=[REDACTED TOKEN] and [REDACTED TOKEN] stay_ok", IssueText.Redact(text));
    }

    [Theory]
    [InlineData("text/plain; charset=utf-8", "text/plain")]
    [InlineData("image/png", "image/png")]
    [InlineData(null, "file")]
    public void Media_type_drops_parameters(string? contentType, string expected)
    {
        Assert.Equal(expected, IssueText.MediaType(contentType));
    }

    [Theory]
    [InlineData("FE-Buddy.log", "text/plain", 100, true)]
    [InlineData("FE-Buddy.log", null, 100, true)]
    [InlineData("notes.TXT", "application/octet-stream", 100, true)]
    [InlineData("screen.png", "image/png", 100, false)]
    [InlineData("huge.log", "text/plain", IssueText.MaxInlineBytes + 1, false)]
    public void Only_small_text_files_are_pasted(string name, string? type, int size, bool expected)
    {
        Assert.Equal(expected, IssueText.CanInline(name, type, size));
    }

    [Theory]
    [InlineData("The airways don't show when the map is open", "airways OR show OR map OR open")]
    [InlineData("FE-BUDDY crashes on launch with ZLC 2610 data", "crashes OR launch OR ZLC OR 2610 OR data")]
    [InlineData("It is a bug", null)]
    public void Search_terms_keep_meaningful_words(string title, string? expected)
    {
        Assert.Equal(expected, IssueText.SearchTerms(title));
    }

    [Fact]
    public void Discord_view_tidies_the_body()
    {
        string body = "<!-- hidden -->\n### What happened?\n\nIt broke.\n\n<details><summary>log</summary>\n\nlots\n</details>\n\n---\n\n<sub>Submitted from the [FE-BUDDY Discord](x) by Discord user `a`.</sub>\n";

        Assert.Equal("**What happened?**\n\nIt broke.\n\n_(collapsed section: open the issue on GitHub)_", IssueText.ForDiscord(body));
    }

    [Fact]
    public void Discord_view_is_cut_to_length()
    {
        string text = IssueText.ForDiscord(new string('a', 5000), maxLength: 100);

        Assert.Equal(100, text.Length);
        Assert.EndsWith("_continued on GitHub_", text);
    }
}
