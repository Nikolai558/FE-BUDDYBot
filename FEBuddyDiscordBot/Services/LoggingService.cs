using Discord.Interactions;

namespace FEBuddyDiscordBot.Services;
public class LoggingService
{
    private readonly ILogger<LoggingService> _logger;
    private readonly DiscordSocketClient _discord;
    private readonly CommandService _commands;
    private readonly InteractionService _interactionCommands;

    public LoggingService(ILogger<LoggingService> logger, DiscordSocketClient discord, CommandService commands, InteractionService interactionCommands)
    {
        _logger = logger;
        _discord = discord;
        _commands = commands;
        _interactionCommands = interactionCommands;

        _discord.Log += OnLogAsync;
        _commands.Log += OnLogAsync;
        _interactionCommands.Log += OnLogAsync;

        _logger.LogDebug("Loaded: LoggingService");
    }

    private Task OnLogAsync(LogMessage arg)
    {
        string logText = $"{arg.Source}: {arg.Exception?.ToString() ?? arg.Message}";

        switch (arg.Severity.ToString())
        {
            case "Critical": { _logger.LogCritical(logText); break; }
            case "Warning": { _logger.LogWarning(logText); break; }
            case "Info": { _logger.LogInformation(logText); break; }
            case "Verbose": { _logger.LogInformation(logText); break; }
            case "Debug": { _logger.LogDebug(logText); break; }
            case "Error": { _logger.LogError(logText); break; }
        }

        return Task.CompletedTask;
    }
}
