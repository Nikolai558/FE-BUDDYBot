using System.Globalization;
using System.Threading.Channels;
using Serilog.Core;
using Serilog.Events;
using Serilog.Formatting.Display;

namespace FEBuddyDiscordBot.Services;

/// <summary>
/// Serilog sink that queues log events for <see cref="DiscordLogService"/> to post in the bot log channel.
/// It has no dependencies, so Serilog can create it before anything that logs (see ReadFrom.Services in Program.cs).
/// </summary>
public sealed class DiscordLogSink : ILogEventSink
{
    /// <summary>Entries waiting to be posted. When full (e.g. while disconnected), the oldest are dropped.</summary>
    private const int Capacity = 500;

    // ":l" renders strings without quotes, like the console sink.
    private static readonly MessageTemplateTextFormatter Formatter = new("{Message:lj}", CultureInfo.InvariantCulture);

    private readonly Channel<DiscordLogEntry> _queue;
    private int _dropped;

    public DiscordLogSink()
    {
        _queue = Channel.CreateBounded<DiscordLogEntry>(
            new BoundedChannelOptions(Capacity) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true },
            _ => Interlocked.Increment(ref _dropped));
    }

    public ChannelReader<DiscordLogEntry> Reader => _queue.Reader;

    /// <summary>How many entries were dropped because the queue was full, since the last call.</summary>
    public int TakeDroppedCount() => Interlocked.Exchange(ref _dropped, 0);

    public void Emit(LogEvent logEvent)
    {
        string source = logEvent.Properties.TryGetValue("SourceContext", out LogEventPropertyValue? value) && value is ScalarValue { Value: string s } ? s : "";
        if (!DiscordLogService.ShouldPost(logEvent.Level, source)) return;

        using StringWriter text = new(CultureInfo.InvariantCulture);
        Formatter.Format(logEvent, text);

        string? exception = logEvent.Exception is { } ex ? $"{ex.GetType().Name}: {ex.Message}" : null;
        _queue.Writer.TryWrite(new DiscordLogEntry(logEvent.Timestamp, logEvent.Level, source, logEvent.MessageTemplate.Text, text.ToString(), exception));
    }
}

/// <summary>One log event, rendered, waiting to be posted.</summary>
/// <param name="Template">The message template, e.g. "Command: {User} ({UserId}) ran {Command}". Errors with the same template count as the same kind of error.</param>
/// <param name="Text">The rendered message.</param>
/// <param name="Exception">The exception's type and message, without the stack trace.</param>
public sealed record DiscordLogEntry(DateTimeOffset Timestamp, LogEventLevel Level, string Source, string Template, string Text, string? Exception = null);
