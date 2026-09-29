using System.Text.Json;
using FEBuddyDiscordBot.DataAccess;

namespace FEBuddyDiscordBot.Tests;

public sealed class GitHubApiTests
{
    [Fact]
    public void Comment_json_is_read_including_the_issue_number_and_app()
    {
        const string json = """
            {
              "id": 3456789012,
              "html_url": "https://github.com/Nikolai558/FE-BUDDY/issues/267#issuecomment-3456789012",
              "issue_url": "https://api.github.com/repos/Nikolai558/FE-BUDDY/issues/267",
              "body": "Thanks!",
              "user": { "login": "fe-buddy-bot[bot]", "avatar_url": "https://avatars/x" },
              "created_at": "2026-09-29T06:30:00Z",
              "updated_at": "2026-09-29T06:32:34Z",
              "performed_via_github_app": { "id": 5117902, "slug": "fe-buddy-bot" }
            }
            """;

        GitHubComment comment = JsonSerializer.Deserialize<GitHubComment>(json)!;

        Assert.Equal(267, comment.IssueNumber);
        Assert.Equal(5117902, comment.PerformedViaGitHubApp!.Id);
        Assert.Equal(new DateTimeOffset(2026, 9, 29, 6, 32, 34, TimeSpan.Zero), comment.UpdatedAt);
    }

    [Fact]
    public void Issue_json_is_read_including_who_closed_it()
    {
        const string json = """
            {
              "number": 3, "title": "[BUG] - x", "body": null, "html_url": "https://github.com/o/r/issues/3",
              "state": "closed", "state_reason": "not_planned", "labels": [{ "name": "won't fix" }],
              "user": { "login": "a", "avatar_url": null }, "closed_by": { "login": "Nikolai558", "avatar_url": null }
            }
            """;

        GitHubIssue issue = JsonSerializer.Deserialize<GitHubIssue>(json)!;

        Assert.False(issue.IsOpen);
        Assert.False(issue.IsPullRequest);
        Assert.Equal("Nikolai558", issue.ClosedBy!.Login);
        Assert.Equal(["won't fix"], issue.LabelNames);
    }
}
