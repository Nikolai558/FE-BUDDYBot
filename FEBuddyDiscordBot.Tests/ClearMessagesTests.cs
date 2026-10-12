using Discord;
using FEBuddyDiscordBot.Modules.SlashCommands;

namespace FEBuddyDiscordBot.Tests;

public class ClearMessagesTests
{
    private sealed record FakeMessage(ulong Id, DateTimeOffset CreatedAt) : ISnowflakeEntity;

    [Theory]
    [InlineData("1", 1)]
    [InlineData("50", 50)]
    [InlineData(" 100 ", 100)]
    [InlineData("all", null)]
    [InlineData("ALL", null)]
    public void Amount_is_1_to_100_or_all(string input, int? expected)
    {
        Assert.True(ModerationSlashCommands.TryParseAmount(input, out int? count));
        Assert.Equal(expected, count);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("101")]
    [InlineData("-5")]
    [InlineData("+5")]
    [InlineData("1.5")]
    [InlineData("ten")]
    [InlineData("")]
    public void Anything_else_is_rejected(string input)
    {
        Assert.False(ModerationSlashCommands.TryParseAmount(input, out _));
    }

    [Fact]
    public void Messages_14_days_old_or_older_are_deleted_one_at_a_time()
    {
        DateTimeOffset now = new(2026, 10, 12, 12, 0, 0, TimeSpan.Zero);
        FakeMessage justNow = new(1, now.AddMinutes(-1));
        FakeMessage thirteenDays = new(2, now.AddDays(-13));
        FakeMessage almostFourteen = new(3, now.AddDays(-14).AddMinutes(1)); // inside Discord's limit, but not by enough
        FakeMessage month = new(4, now.AddDays(-30));

        (List<FakeMessage> recent, List<FakeMessage> old) = ModerationSlashCommands.SplitByAge([justNow, thirteenDays, almostFourteen, month], now);

        Assert.Equal([justNow, thirteenDays], recent);
        Assert.Equal([almostFourteen, month], old);
    }
}
