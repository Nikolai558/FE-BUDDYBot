namespace FEBuddyDiscordBot.Services;

/// <summary>
/// Forwards Discord.Net's own log messages into the app's logger.
/// </summary>
public sealed class LoggingService
{
    private readonly ILogger<LoggingService> _logger;

    public LoggingService(ILogger<LoggingService> logger, DiscordSocketClient discord, InteractionService interactions)
    {
        _logger = logger;

        discord.Log += OnLogAsync;
        interactions.Log += OnLogAsync;
    }

    private Task OnLogAsync(LogMessage message)
    {
        LogLevel level = message.Severity switch
        {
            LogSeverity.Critical => LogLevel.Critical,
            LogSeverity.Error => LogLevel.Error,
            LogSeverity.Warning => LogLevel.Warning,
            LogSeverity.Info => LogLevel.Information,
            LogSeverity.Verbose => LogLevel.Debug,
            _ => LogLevel.Trace,
        };

        // Discord asks bots to reconnect routinely; Discord.Net logs that as a warning with an exception, which is just noise.
        Exception? exception = message.Exception;
        if (exception is GatewayReconnectException)
        {
            level = LogLevel.Information;
            exception = null;
        }

        _logger.Log(level, exception, "{Source}: {Message}", message.Source, message.Message ?? message.Exception?.Message);
        return Task.CompletedTask;
    }
}
