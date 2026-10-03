using FEBuddyDiscordBot.Issues;

namespace FEBuddyDiscordBot.Tests;

/// <summary>Discord rejects a whole modal if any part breaks its limits, so check them all here.</summary>
public sealed class IssueTemplatesTests
{
    public static TheoryData<IssueKind> Kinds() => [.. Enum.GetValues<IssueKind>()];

    [Theory]
    [MemberData(nameof(Kinds))]
    public void Each_modal_has_at_most_five_questions(IssueKind kind)
    {
        IssueTemplate template = IssueTemplate.For(kind);

        Assert.True(template.Page(1).Count() + 1 <= 5, "Page 1 (plus the title) has too many questions");
        Assert.True(template.Page(2).Count() <= 5, "Page 2 has too many questions");
        Assert.True(template.Page(2).Any(), "Page 2 is empty, and Discord won't open an empty modal");
        Assert.All(template.Fields, f => Assert.InRange(f.Page, 1, 2));
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void Fields_fit_discord_limits(IssueKind kind)
    {
        IssueTemplate template = IssueTemplate.For(kind);

        Assert.Equal(template.Fields.Count, template.Fields.Select(f => f.Id).Distinct().Count());
        Assert.True($"{template.Name} (1/2)".Length <= 45);

        foreach (IssueField field in template.Fields)
        {
            Assert.True(field.Label.Length <= 45, $"{field.Id}: label too long");
            Assert.True((field.Description?.Length ?? 0) <= 100, $"{field.Id}: description too long");
            Assert.True((field.Placeholder?.Length ?? 0) <= 100, $"{field.Id}: placeholder too long");
            Assert.NotEqual("title", field.Id);

            if (field.Input is FieldInput.Select or FieldInput.MultiSelect or FieldInput.Radio or FieldInput.Confirm)
            {
                Assert.NotNull(field.Options);
                Assert.InRange(field.Options!.Count, 1, 25);
                Assert.All(field.Options!, o => Assert.True(o.Length <= 100, $"{field.Id}: option too long"));
            }
        }
    }

    [Fact]
    public void Every_template_labels_its_type()
    {
        Assert.Contains("bug", IssueTemplate.For(IssueKind.Bug).Labels);
        Assert.Contains("feature", IssueTemplate.For(IssueKind.Feature).Labels);
        Assert.Contains("docs", IssueTemplate.For(IssueKind.Docs).Labels);
        Assert.Contains("task", IssueTemplate.For(IssueKind.Task).Labels);
        Assert.True(IssueTemplate.For(IssueKind.Task).RestrictedToDevTaskRoles);
    }
}
