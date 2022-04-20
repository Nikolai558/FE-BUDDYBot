using Discord.Interactions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FEBuddyDiscordBot.Modules;
public class InteractionModule: InteractionModuleBase<SocketInteractionContext>
{

    private readonly IServiceProvider _services;
    private readonly IConfiguration _config;
    private readonly DiscordSocketClient _discord;
    private readonly ILogger _logger;

    public InteractionModule(IServiceProvider services)
    {
        _services = services;
        _config = _services.GetRequiredService<IConfiguration>();
        _discord = _services.GetRequiredService<DiscordSocketClient>();
        _logger = _services.GetRequiredService<ILogger<InteractionModule>>();

        _logger.LogDebug("Module: Loaded InteractionModule");
    }


    [Discord.Interactions.RequireOwner]
    [SlashCommand("ping", "Receive a ping message! ")]
    public async Task PingCommand()
    {
        await RespondAsync("PING!");
    }
}
