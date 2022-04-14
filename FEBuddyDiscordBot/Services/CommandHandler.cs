namespace FEBuddyDiscordBot.Services;
public class CommandHandler
{
    private readonly IServiceProvider _services;
    private readonly IConfiguration _config;
    private readonly DiscordSocketClient _discord;
    private readonly CommandService _commands;
    private readonly ILogger _logger;

    public CommandHandler(IServiceProvider services)
    {
        _services = services;
        _config = _services.GetRequiredService<IConfiguration>();
        _discord = _services.GetRequiredService<DiscordSocketClient>();
        _commands = _services.GetRequiredService<CommandService>();
        _logger = _services.GetRequiredService<ILogger<CommandHandler>>();

        _discord.MessageReceived += HandleCommand;

        _logger.LogInformation("Loaded: CommandHandler");
    }

    private async Task HandleCommand(SocketMessage arg)
    {
        var message = arg as IUserMessage;

        if (message == null) return;

        if (message.Source != Discord.MessageSource.User) return;

        int argPosition = 0;

        var context = new CommandContext(_discord, message);

        char prefix = Char.Parse(_config.GetSection("DefaultBotSettings").GetSection("Prefix").Value);

        if(!message.HasCharPrefix(prefix, ref argPosition)) return;

        var result = await _commands.ExecuteAsync(context, argPosition, _services);

        await LogCommandUse(context, result);

        if (!result.IsSuccess)
        {
            if (result.ErrorReason != "Unknown command.")
            {
                await message.Channel.SendMessageAsync($"**Error:** {result.ErrorReason}");
            }
            else
            {
                await message.Author.SendMessageAsync($"**Error: {result.ErrorReason}**\n\t'{message.Content}'");
            }
        }
    }

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
