using FEBuddyDiscordBot.Issues;

namespace FEBuddyDiscordBot.Tests;

public sealed class DiscordReplyTests
{
    private static string Convert(string content) => DiscordReply.ToGitHubMarkdown(
        content,
        id => id == 1 ? "Nikolas Boling | ZLC" : null,
        id => id == 2 ? "admin" : null,
        id => id == 3 ? "fe-buddy-issues" : null);

    [Theory]
    [InlineData("hey <@1> and <@!1>", "hey @​Nikolas Boling | ZLC and @​Nikolas Boling | ZLC")]
    [InlineData("<@9> left", "@​unknown-user left")]
    [InlineData("ping <@&2>", "ping @​admin")]
    [InlineData("see <#3>", "see #fe-buddy-issues")]
    [InlineData("nice <:feb:123456> <a:spin:789>", "nice :feb: :spin:")]
    [InlineData("at <t:1790000000:f>", "at 2026-09-21 14:13 UTC")]
    [InlineData("@octocat look", "@​octocat look")]
    public void Discord_markup_becomes_plain_github_text(string content, string expected)
    {
        Assert.Equal(expected, Convert(content));
    }

    [Theory]
    [InlineData("<t:99999999999999999>", "<t:99999999999999999>")]
    [InlineData("<@123456789012345678901234567890>", "@​unknown-user")]
    public void Impossible_ids_and_times_do_not_throw(string content, string expected)
    {
        Assert.Equal(expected, Convert(content));
    }

    [Fact]
    public void Tokens_in_messages_are_redacted()
    {
        Assert.Equal("my token [REDACTED TOKEN]", Convert("my token ghp_" + new string('a', 36)));
    }

    [Fact]
    public void Comment_credits_the_author_and_links_the_message()
    {
        string comment = DiscordReply.BuildComment(new ReplyAuthor("Nik *ZLC*", "nikolai558"), "It still crashes.", "https://discord.com/channels/1/2/3", []);

        Assert.Equal("**Nik \\*ZLC\\*** (Discord user `nikolai558`) [on Discord](https://discord.com/channels/1/2/3):\n\nIt still crashes.\n", comment);
    }

    [Fact]
    public void Linked_github_account_is_shown_without_pinging()
    {
        string comment = DiscordReply.BuildComment(new ReplyAuthor("Nik", "nikolai558", "Nikolai558"), "Hi", "u", []);

        Assert.StartsWith("**Nik** ([Nikolai558](https://github.com/Nikolai558), Discord user `nikolai558`) [on Discord](u):", comment);
    }

    [Fact]
    public void Replies_quote_the_message_they_answer()
    {
        string comment = DiscordReply.BuildComment(new ReplyAuthor("Nik", "nik"), "Yes", "u", [],
            new ReplyQuote("Someone", "Does it\nstill crash?"));

        Assert.Contains("> Replying to **Someone**: Does it still crash?\n\nYes\n", comment);
    }

    [Fact]
    public void Files_are_listed_and_text_files_pasted()
    {
        string comment = DiscordReply.BuildComment(new ReplyAuthor("Nik", "nik"), "", "https://m",
            [new DraftAttachment("FE-Buddy.log", "text/plain; charset=utf-8", 20, "https://cdn/x", "log line")]);

        Assert.Contains("- 📎 `FE-Buddy.log` (text/plain, 20 B) was attached on Discord: [view it on Discord](https://m)", comment);
        Assert.Contains("```text\nlog line\n```", comment);
    }
}
