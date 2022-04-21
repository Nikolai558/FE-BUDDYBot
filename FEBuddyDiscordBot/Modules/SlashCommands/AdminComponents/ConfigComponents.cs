using Discord.Interactions;
using FEBuddyDiscordBot.DataAccess.DB;
using FEBuddyDiscordBot.Modals;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FEBuddyDiscordBot.Modules.SlashCommands.AdminComponents;
public class ConfigComponents : InteractionModuleBase<SocketInteractionContext>
{
    // Dependency Injection Services Required
    private readonly IServiceProvider _services;
    private readonly IConfiguration _config;
    private readonly DiscordSocketClient _discord;
    private readonly ILogger _logger;
    private readonly IMongoGuildData _guildData;

    public ConfigComponents(IServiceProvider services)
    {
        _services = services;
        _config = _services.GetRequiredService<IConfiguration>();
        _discord = _services.GetRequiredService<DiscordSocketClient>();
        _logger = _services.GetRequiredService<ILogger<ConfigComponents>>();
        _guildData = _services.GetRequiredService<IMongoGuildData>();

        _logger.LogDebug("Module: Loaded ConfigComponents");
    }

    [ComponentInteraction("button")]
    public async Task HandleButtonInput()
    {
        await RespondWithModalAsync<ConfigModal_DiscordEvents>("config_modal");
    }

    [ComponentInteraction("configMenu")]
    public async Task HandleMenuSelection(string option)
    {
        switch (option)
        {
            case "changeDiscordEvents": await RespondWithModalAsync<ConfigModal_DiscordEvents>("configModal_DiscordEvents");  break;
            case "changeRoleNames": await RespondWithModalAsync<ConfigModal_RoleNames>("configModal_RoleNames"); break;
            case "changeChannelNames": await RespondWithModalAsync<ConfigModal_ChannelNames>("configModal_ChannelNames"); break;
            default: await RespondAsync("Invalid Menu Option...Something went wrong, sorry."); break;
        }
    }

    [ModalInteraction("configModal_DiscordEvents")]
    public async Task ChangeDiscordEventsConfig(ConfigModal_DiscordEvents modal)
    {
        await DeferAsync(ephemeral: true);
        await FollowupAsync("Nothing really changed, this functionality is still under development...", ephemeral: true);
    }

    [ModalInteraction("configModal_RoleNames")]
    public async Task ChangeRoleNamesConfig(ConfigModal_RoleNames modal)
    {
        await DeferAsync(ephemeral: true);
        await FollowupAsync("Nothing really changed, this functionality is still under development...", ephemeral: true);
    }

    [ModalInteraction("configModal_ChannelNames")]
    public async Task ChangeChannelNamesConfig(ConfigModal_ChannelNames modal)
    {
        await DeferAsync(ephemeral: true);
        await FollowupAsync("Nothing really changed, this functionality is still under development...", ephemeral: true);
    }
}
