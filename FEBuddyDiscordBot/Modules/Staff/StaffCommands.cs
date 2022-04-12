using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FEBuddyDiscordBot.Modules.Staff;

[Name("Staff Commands")]
[Summary("These commands are to assist the Staff of the server.")]
[RequireUserPermission(GuildPermission.ManageMessages | GuildPermission.ManageChannels)]
public class StaffCommands : ModuleBase
{
    private readonly IServiceProvider _services;
    private readonly IConfigurationRoot _config;
    private readonly DiscordSocketClient _discord;
    private readonly ILogger _logger;
    private readonly string _prefix;

    public StaffCommands(IServiceProvider services)
    {
        _services = services;
        _config = _services.GetRequiredService<IConfigurationRoot>();
        _discord = _services.GetRequiredService<DiscordSocketClient>();
        _logger = _services.GetRequiredService<ILogger<StaffCommands>>();
        _prefix = _config["prefix"];

        _logger.LogInformation("Module: Loaded StaffCommands");
    }

    // Discord Server Staff commands go here.
}
