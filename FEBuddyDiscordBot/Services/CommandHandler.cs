using FEBuddyDiscordBot.DataAccess.DB;
using FEBuddyDiscordBot.Models;

namespace FEBuddyDiscordBot.Services;

/// <summary>
/// Handle Discord Prefix Commands
/// </summary>
public class CommandHandler
{
    // Dependency Injection Services Required
    private readonly IServiceProvider _services;
    private readonly IConfiguration _config;
    private readonly DiscordSocketClient _discord;
    private readonly CommandService _commands;
    private readonly ILogger _logger;
    private readonly IMongoGuildData _guildData;

    /// <summary>
    /// Constructor for Command Handler
    /// </summary>
    /// <param name="services">Dependency Injection Service Provider</param>
    public CommandHandler(IServiceProvider services)
    {
        _services = services;
        _config = _services.GetRequiredService<IConfiguration>();
        _discord = _services.GetRequiredService<DiscordSocketClient>();
        _commands = _services.GetRequiredService<CommandService>();
        _logger = _services.GetRequiredService<ILogger<CommandHandler>>();
        _guildData = _services.GetRequiredService<IMongoGuildData>();

        _discord.MessageReceived += HandleCommand;

        _logger.LogDebug("Loaded: CommandHandler");
    }

    /// <summary>
    /// Handle Discord Messages that have the correct prefix and execute the command received.
    /// </summary>
    /// <param name="arg">Socket Message from discord.</param>
    /// <returns>None</returns>
    private async Task HandleCommand(SocketMessage arg)
    {
        GuildModel? guild = null;
        IUserMessage? message = arg as IUserMessage;

        if (message == null) return;

        if (message.Source != MessageSource.User) return; // Only respond to user messages

        SocketGuildChannel? channel = message.Channel as SocketGuildChannel;

        if (channel != null) // Make sure we are in a discord server channel (not DM to bot)
        {
            guild = await _guildData.GetGuildAsync(channel.Guild.Id); // Load configuration from Database
        }

        int argPosition = 0; // Prefix position in message

        var context = new CommandContext(_discord, message); // Create a new context for the message

        char prefix = Char.Parse(guild?.Settings.Prefix ?? _config.GetRequiredSection("DefaultBotSettings").GetRequiredSection("Prefix").Value);

        if (!message.HasCharPrefix(prefix, ref argPosition)) return; // Message does not have the guild command prefix so do nothing

        var result = await _commands.ExecuteAsync(context, argPosition, _services); // Execute the command

        await LogCommandUse(context, result); // Log the command usage

        if (!result.IsSuccess)
        {
            if (result.ErrorReason != "Unknown command.")
            {
                await message.Author.SendMessageAsync($"**Error:** {result.ErrorReason}"); // Tell the user the command faild.
            }
        }
    }

    /// <summary>
    /// Log what user did what command in what server
    /// </summary>
    /// <param name="context">Context for the message the command is in</param>
    /// <param name="result">IResult for the result of the command</param>
    /// <returns>None</returns>
    private async Task LogCommandUse(CommandContext context, IResult result)
    {
        await Task.Run(() =>
        {
            if (context.Channel is IGuildChannel)
            {
                var logText = $"User: {context.User.Username} ({context.User.Id}) Discord Server: [{context.Guild.Name}] -> [{context.Message.Content}]";
                _logger.LogInformation(logText);
            }
            else
            {
                var logText = $"User: {context.User.Username} ({context.User.Id}) Private Message: -> [{context.Message.Content}]";
                _logger.LogInformation(logText);
            }
        });
    }
}
