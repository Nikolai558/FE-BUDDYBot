using FEBuddyDiscordBot.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;

namespace FEBuddyDiscordBot.Tests;

public class DiscordLogTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 12, 12, 0, 0, TimeSpan.Zero);

    private static DiscordLogEntry Entry(string text, LogEventLevel level = LogEventLevel.Information, string? template = null, string? exception = null) =>
        new(Now, level, "FEBuddyDiscordBot.Services.SomeService", template ?? text, text, exception);

    [Theory]
    [InlineData(LogEventLevel.Debug, false)]
    [InlineData(LogEventLevel.Information, true)]
    [InlineData(LogEventLevel.Error, true)]
    public void App_logs_are_posted_from_Information_up(LogEventLevel level, bool posted)
    {
        Assert.Equal(posted, DiscordLogService.ShouldPost(level, "FEBuddyDiscordBot.Services.InteractionHandler"));
    }

    [Theory]
    [InlineData(LogEventLevel.Information, false)]
    [InlineData(LogEventLevel.Warning, true)]
    public void DiscordNet_logs_are_posted_from_Warning_up(LogEventLevel level, bool posted)
    {
        Assert.Equal(posted, DiscordLogService.ShouldPost(level, typeof(LoggingService).FullName!));
    }

    [Fact]
    public void The_log_service_never_posts_its_own_logs()
    {
        Assert.False(DiscordLogService.ShouldPost(LogEventLevel.Fatal, typeof(DiscordLogService).FullName!));
    }

    [Fact]
    public void Each_kind_of_error_pings_once_per_cooldown()
    {
        Dictionary<string, DateTimeOffset> last = [];
        DiscordLogEntry error = Entry("GitHub: request failed", LogEventLevel.Error, "GitHub: {What} failed");
        DiscordLogEntry other = Entry("VATUSA: request failed", LogEventLevel.Error, "VATUSA: {What} failed");

        Assert.True(DiscordLogService.ShouldAlert(last, error, Now));
        Assert.False(DiscordLogService.ShouldAlert(last, error, Now.AddMinutes(29)));
        Assert.True(DiscordLogService.ShouldAlert(last, other, Now.AddMinutes(1)));
        Assert.True(DiscordLogService.ShouldAlert(last, error, Now.Add(DiscordLogService.AlertCooldown)));
    }

    [Theory]
    [InlineData(LogEventLevel.Information)]
    [InlineData(LogEventLevel.Warning)]
    public void Below_Error_never_pings(LogEventLevel level)
    {
        Assert.False(DiscordLogService.ShouldAlert([], Entry("Roles: something", level), Now));
    }

    [Fact]
    public void Line_shows_level_time_and_bold_category()
    {
        string line = DiscordLogService.FormatLine(Entry("Command: nikolai558 (1) ran /admin settings"), 1, []);
        Assert.Equal($"▫️ <t:{Now.ToUnixTimeSeconds()}:T> **Command** nikolai558 (1) ran \\/admin settings", line);
    }

    [Fact]
    public void Markdown_in_names_is_escaped()
    {
        string line = DiscordLogService.FormatLine(Entry("Command: some_user_ ran /x"), 1, []);
        Assert.Contains(@"some\_user\_", line);
    }

    [Fact]
    public void Exception_goes_on_a_subtext_line()
    {
        string line = DiscordLogService.FormatLine(Entry("Issues: sync failed", LogEventLevel.Error, exception: "HttpRequestException: timed out"), 1, []);
        Assert.StartsWith("❌ ", line);
        Assert.EndsWith("\n-# ↳ HttpRequestException\\: timed out", line);
    }

    [Fact]
    public void Repeats_in_a_row_become_one_line_with_a_count()
    {
        List<string> lines = DiscordLogService.FormatLines([Entry("A: x"), Entry("A: x"), Entry("A: x"), Entry("B: y"), Entry("A: x")], []);

        Assert.Equal(3, lines.Count);
        Assert.EndsWith("(×3)", lines[0]);
        Assert.DoesNotContain("×", lines[1]);
        Assert.DoesNotContain("×", lines[2]);
    }

    [Fact]
    public void Secrets_are_redacted()
    {
        string text = "token MTIzNDU2.abc.def webhook https://discord.com/api/webhooks/123/abc-DEF_1 gh ghs_abcdefghijklmnopqrstuvwxyz0123";
        string redacted = DiscordLogService.Redact(text, ["MTIzNDU2.abc.def"]);
        Assert.Equal("token [redacted] webhook [redacted] gh [redacted]", redacted);
    }

    [Fact]
    public void Lines_are_packed_into_messages_up_to_the_limit()
    {
        List<string> messages = DiscordLogService.Chunk(["aaaa", "bbbb", "cccc", new string('d', 30)], 10);

        Assert.Equal(["aaaa\nbbbb", "cccc", new string('d', 9) + "…"], messages);
        Assert.All(messages, m => Assert.True(m.Length <= 10));
    }

    [Theory]
    [InlineData("1.0.0+5f2c9e1a7b3d", "1.0.0 (5f2c9e1)")]
    [InlineData("1.0.0+abc", "1.0.0 (abc)")]
    [InlineData("1.0.0", "1.0.0")]
    [InlineData("1.0.0+", "1.0.0")]
    [InlineData(null, "(unknown version)")]
    public void Version_shows_the_short_commit(string? informational, string expected)
    {
        Assert.Equal(expected, StartupService.Version(informational));
    }
}

public class DiscordLogSinkWiringTests
{
    // Program.cs registers the sink in DI and relies on Serilog's ReadFrom.Services to pick it up.
    [Fact]
    public void App_logs_reach_the_sink_rendered_without_quotes()
    {
        ServiceCollection services = new();
        services.AddSingleton<DiscordLogSink>();
        services.AddSingleton<Serilog.Core.ILogEventSink>(s => s.GetRequiredService<DiscordLogSink>());
        services.AddLogging();
        services.AddSerilog((s, logger) => logger.MinimumLevel.Debug().ReadFrom.Services(s));

        using ServiceProvider provider = services.BuildServiceProvider();
        ILogger<InteractionHandler> logger = provider.GetRequiredService<ILogger<InteractionHandler>>();
        logger.LogDebug("Command: debug is not posted");
        logger.LogInformation("Command: {User} ran {Command}", "nikolai558", "/admin settings");

        DiscordLogSink sink = provider.GetRequiredService<DiscordLogSink>();
        Assert.True(sink.Reader.TryRead(out DiscordLogEntry? entry));
        Assert.Equal("Command: nikolai558 ran /admin settings", entry.Text);
        Assert.Equal("Command: {User} ran {Command}", entry.Template);
        Assert.Equal(typeof(InteractionHandler).FullName, entry.Source);
        Assert.False(sink.Reader.TryRead(out _));
    }
}
