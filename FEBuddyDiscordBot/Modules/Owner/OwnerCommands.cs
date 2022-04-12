using Discord.Commands;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FEBuddyDiscordBot.Modules.Owner;

[Name("Bot Owner Commands")]
[Summary("These commands only effect the hardware of the computer that the bot is running on. Therfore only the bot Owner can run these commands.")]
[RequireOwner]
public class OwnerCommands : ModuleBase
{
    private readonly IServiceProvider _services;
    private readonly IConfigurationRoot _config;
    private readonly DiscordSocketClient _discord;
    private readonly ILogger _logger;
    private readonly string _prefix;

    public OwnerCommands(IServiceProvider services)
    {
        _services = services;
        _config = _services.GetRequiredService<IConfigurationRoot>();
        _discord = _services.GetRequiredService<DiscordSocketClient>();
        _logger = _services.GetRequiredService<ILogger<OwnerCommands>>();
        _prefix = _config["prefix"];

        _logger.LogInformation("Module: Loaded OwnerCommands");
    }

    // Bot Owner Only Commands go here.
}
