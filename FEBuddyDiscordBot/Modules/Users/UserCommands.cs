using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FEBuddyDiscordBot.Modules.Users;

[Name("User Commands")]
[Summary("Helpful commands for All users in the Discord server!")]
public class UserCommands : ModuleBase
{
    private readonly IServiceProvider _services;
    private readonly IConfigurationRoot _config;
    private readonly DiscordSocketClient _discord;
    private readonly ILogger _logger;
    private readonly string _prefix;

    public UserCommands(IServiceProvider services)
    {
        _services = services;
        _config = _services.GetRequiredService<IConfigurationRoot>();
        _discord = _services.GetRequiredService<DiscordSocketClient>();
        _logger = _services.GetRequiredService<ILogger<UserCommands>>();
        _prefix = _config["prefix"];

        _logger.LogInformation("Module: Loaded UserCommands");
    }

    // Discord Server User commands go here.
}
