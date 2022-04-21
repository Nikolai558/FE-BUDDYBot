using Discord.Interactions;

namespace FEBuddyDiscordBot.Services;

/// <summary>
/// Logging service for all your logging needs.
/// </summary>
public class LoggingService
{
    // Dependency Injection Services required.
    private readonly ILogger<LoggingService> _logger;
    private readonly DiscordSocketClient _discord;
    private readonly CommandService _commands;
    private readonly InteractionService _interactionCommands;

    /// <summary>
    /// Constructor for the Logging Service
    /// </summary>
    /// <param name="logger">ILogger Service</param>
    /// <param name="discord">Discord Socket Client</param>
    /// <param name="commands">Discord Command Service</param>
    /// <param name="interactionCommands">Discord Interaction Service</param>
    public LoggingService(ILogger<LoggingService> logger, DiscordSocketClient discord, CommandService commands, InteractionService interactionCommands)
    {
        _logger = logger;
        _discord = discord;
        _commands = commands;
        _interactionCommands = interactionCommands;

        // Handle different Discord Events 
        _discord.Log += OnLogAsync;
        _commands.Log += OnLogAsync;
        _interactionCommands.Log += OnLogAsync;

        _logger.LogDebug("Loaded: LoggingService");
    }

    /// <summary>
    /// Log Message Async 
    /// </summary>
    /// <param name="arg">Log Message to be logged.</param>
    /// <returns>Task.CompletedTask</returns>
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
