using FEBuddyDiscordBot.Issues;

namespace FEBuddyDiscordBot.Tests;

public sealed class IssueTagsTests
{
    private static IReadOnlyList<string> Tags(string[] labels, string title = "[BUG] - Something", string? body = null, bool open = true, string? reason = null) =>
        IssueTags.ForIssue(labels, title, body, open, reason);

    [Fact]
    public void Type_status_and_version_labels_become_tags_in_order()
    {
        Assert.Equal(["Bug", "Priority", "In progress", "v3.x"], Tags(["v3.x", "in progress", "bug", "priority"]));
    }

    [Theory]
    [InlineData("feature", "Feature")]
    [InlineData("docs", "Docs")]
    [InlineData("task", "Dev task")]
    public void Each_type_label_has_a_tag(string label, string tag)
    {
        Assert.Equal(tag, Tags([label])[0]);
    }

    [Fact]
    public void Dev_task_without_label_is_found_by_title_prefix()
    {
        Assert.Equal(["Dev task", "v3.x"], Tags(["v3.x"], title: "[TASK] - Split the map view"));
    }

    [Fact]
    public void Dev_task_without_label_is_found_by_kind_of_work_section()
    {
        Assert.Equal("Dev task", Tags([], title: "Split the map view", body: "### Kind of work\n\nTests\n")[0]);
    }

    [Fact]
    public void Issue_without_type_needs_triage()
    {
        Assert.Equal(["Needs triage"], Tags([], title: "Something odd"));
    }

    [Theory]
    [InlineData(new string[0], "completed", "Fixed")]
    [InlineData(new string[0], "not_planned", "Won't fix")]
    [InlineData(new[] { "won't fix" }, "completed", "Won't fix")]
    [InlineData(new[] { "duplicate" }, "not_planned", "Duplicate")]
    [InlineData(new string[0], "duplicate", "Duplicate")]
    [InlineData(new[] { "invalid" }, "not_planned", "Invalid")]
    public void Closed_issues_get_a_reason_tag(string[] labels, string reason, string expected)
    {
        IReadOnlyList<string> tags = Tags(["bug", .. labels], open: false, reason: reason);
        Assert.Equal(expected, tags[1]);
    }

    [Fact]
    public void At_most_five_tags_keeping_the_most_important()
    {
        IReadOnlyList<string> tags = Tags(["bug", "feature", "priority", "in progress", "partially done", "planned", "v3.x"]);

        Assert.Equal(["Bug", "Feature", "Priority", "In progress", "Partially done"], tags);
    }

    [Fact]
    public void Every_mapped_tag_exists_and_the_forum_limit_holds()
    {
        HashSet<string> defined = IssueTags.All.Select(t => t.Name).ToHashSet();
        string[] everyLabel = ["bug", "feature", "docs", "task", "priority", "in progress", "partially done", "planned", "needs research", "question", "help wanted", "v3.x", "v2.x"];

        foreach (string label in everyLabel)
        {
            Assert.All(Tags([label], title: "x"), tag => Assert.Contains(tag, defined));
        }

        Assert.True(IssueTags.All.Count <= 20);
    }
}
