using Discord.Interactions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace FEBuddyDiscordBot.Services;
public class InteractionHandler
{
    private readonly IServiceProvider _services;
    private readonly DiscordSocketClient _discord;
    private readonly InteractionService _interactionCommands;
    private readonly ILogger _logger;

    public InteractionHandler(IServiceProvider services)
    {
        _services = services;
        _discord = _services.GetRequiredService<DiscordSocketClient>();
        _interactionCommands = _services.GetRequiredService<InteractionService>();
        _logger = _services.GetRequiredService<ILogger<InteractionHandler>>();

        _discord.Ready += InitializeAsync;

        _logger.LogInformation("Loaded: InteractionHandler");
    }

    private async Task InitializeAsync()
    {
        await _interactionCommands.AddModulesAsync(Assembly.GetEntryAssembly(), _services);

        _discord.InteractionCreated += HandleInteraction;
    }

    private async Task HandleInteraction(SocketInteraction arg)
    {
        try
        {
            var context = new SocketInteractionContext(_discord, arg);
            await _interactionCommands.ExecuteCommandAsync(context, _services);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Interaction Handler: " + ex.Message);
            throw;
        }
    }
}
