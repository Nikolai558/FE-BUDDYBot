using FEBuddyDiscordBot.Airac;
using FEBuddyDiscordBot.Models;

namespace FEBuddyDiscordBot.Tests;

public class AiracTests
{
    [Theory]
    [InlineData("2026-01-22", "2601", "2026-01-22")]
    [InlineData("2026-10-12", "2610", "2026-10-01")]
    [InlineData("2026-10-28", "2610", "2026-10-01")]
    [InlineData("2026-10-29", "2611", "2026-10-29")]
    [InlineData("2026-01-21", "2513", "2025-12-25")]
    [InlineData("2020-12-31", "2014", "2020-12-31")] // a 14th cycle
    [InlineData("2027-01-21", "2701", "2027-01-21")]
    public void Current_cycle_matches_the_published_airac_dates(string date, string id, string effective)
    {
        AiracCycle cycle = AiracCycle.Current(DateOnly.Parse(date));

        Assert.Equal(id, cycle.Id);
        Assert.Equal(DateOnly.Parse(effective), cycle.EffectiveDate);
    }

    [Theory]
    [InlineData("2026-10-12", "2611")]
    [InlineData("2026-10-28", "2611")]
    [InlineData("2026-10-29", "2612")]
    [InlineData("2026-12-23", "2613")]
    [InlineData("2026-12-24", "2701")]
    public void Next_cycle_changes_on_the_effective_date(string date, string id)
    {
        Assert.Equal(id, AiracCycle.Next(DateOnly.Parse(date)).Id);
    }

    [Theory]
    [InlineData("1200z, 2300z", "1200z, 2300z")]
    [InlineData("1200z", "1200z")]
    [InlineData("2300Z,1200z 0600", "0600z, 1200z, 2300z")]
    [InlineData("12:00z; 12:00 1200z", "1200z")]
    [InlineData("0000z, 2359z", "0000z, 2359z")]
    [InlineData("off", "off")]
    [InlineData(" OFF ", "off")]
    public void Check_times_are_read_sorted_and_without_duplicates(string input, string expected)
    {
        Assert.True(MetafileCheckTimes.TryParse(input, out TimeOnly[] times, out string? error), error);
        Assert.Equal(expected, MetafileCheckTimes.Format(times));
    }

    [Theory]
    [InlineData("", "at least one")]
    [InlineData("2400z", "`2400z`")]
    [InlineData("1260z", "`1260z`")]
    [InlineData("12z", "`12z`")]
    [InlineData("1200z, noon", "`noon`")]
    [InlineData("1200z, off", "`off`")]
    public void Bad_check_times_are_rejected_naming_the_bad_part(string input, string mentions)
    {
        Assert.False(MetafileCheckTimes.TryParse(input, out _, out string? error));
        Assert.Contains(mentions, error);
    }

    [Fact]
    public void At_most_48_check_times()
    {
        string every30 = string.Join(",", Enumerable.Range(0, 48).Select(i => $"{i / 2:00}{i % 2 * 30:00}z"));
        Assert.True(MetafileCheckTimes.TryParse(every30, out _, out _));
        Assert.False(MetafileCheckTimes.TryParse(every30 + ",0015z", out _, out string? error));
        Assert.Contains("48", error);
    }

    [Fact]
    public void Next_check_is_the_next_listed_utc_time_rolling_over_to_tomorrow()
    {
        TimeOnly[] times = [new(12, 0), new(23, 0)];

        Assert.Equal(Utc("2026-10-12T12:00"), MetafileCheckTimes.NextAfter(Utc("2026-10-12T03:00"), times));
        Assert.Equal(Utc("2026-10-12T23:00"), MetafileCheckTimes.NextAfter(Utc("2026-10-12T12:00"), times));
        Assert.Equal(Utc("2026-10-13T12:00"), MetafileCheckTimes.NextAfter(Utc("2026-10-12T23:00"), times));
        Assert.Equal(Utc("2026-10-13T12:00"), MetafileCheckTimes.NextAfter(Utc("2026-10-12T12:00"), [new(12, 0)]));
        Assert.Null(MetafileCheckTimes.NextAfter(Utc("2026-10-12T12:00"), []));
    }

    [Fact]
    public void Settings_saved_before_check_times_existed_get_the_default()
    {
        GuildSettings settings = System.Text.Json.JsonSerializer.Deserialize<GuildSettings>("""{"AiracPingRoleId":1}""")!;

        Assert.Equal("1500z, 2100z", MetafileCheckTimes.Format(settings.AiracCheckTimesUtc));
    }

    [Fact]
    public void Check_times_survive_a_save()
    {
        GuildSettings settings = new() { AiracCheckTimesUtc = [new(6, 30), new(18, 0)] };
        GuildSettings loaded = System.Text.Json.JsonSerializer.Deserialize<GuildSettings>(System.Text.Json.JsonSerializer.Serialize(settings))!;

        Assert.Equal(settings.AiracCheckTimesUtc, loaded.AiracCheckTimesUtc);
    }

    [Fact]
    public void Ping_mentions_the_role_and_names_the_cycle_and_effective_date()
    {
        string text = MetafilePing.Text(new AiracCycle("2611", new DateOnly(2026, 11, 26)), 1234);

        Assert.StartsWith("<@&1234>\n## 📢 AIRAC 2611 MetaFile is available", text);
        Assert.Contains("November 26, 2026", text);
        Assert.DoesNotContain("/airac-pings", text);
    }

    private static DateTimeOffset Utc(string value) => DateTimeOffset.Parse(value + "Z", System.Globalization.CultureInfo.InvariantCulture);
}
