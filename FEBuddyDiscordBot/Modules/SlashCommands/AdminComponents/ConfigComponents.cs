using Discord.Interactions;
using FEBuddyDiscordBot.DataAccess.DB;
using FEBuddyDiscordBot.Modals;
using FEBuddyDiscordBot.Models;

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
            case "changeDiscordEvents": await RespondWithModalAsync<ConfigModal_DiscordEvents>("configModal_DiscordEvents"); break;
            case "changeRoleNames": await RespondWithModalAsync<ConfigModal_RoleNames>("configModal_RoleNames"); break;
            case "changeChannelNames": await RespondWithModalAsync<ConfigModal_ChannelNames>("configModal_ChannelNames"); break;
            default: await RespondAsync("Invalid Menu Option...Something went wrong, sorry."); break;
        }
    }

    [ModalInteraction("configModal_DiscordEvents")]
    public async Task ChangeDiscordEventsConfig(ConfigModal_DiscordEvents modal)
    {
        await DeferAsync(ephemeral: true);
        GuildModel guild = await _guildData.GetGuildAsync(Context.Guild.Id);

        try
        {
            guild.Settings.AutoAssignRoles_OnJoin = bool.Parse(modal.AutoAssignRoles_OnJoin);
            guild.Settings.AutoAssignRoles_OnVoiceChannelJoin = bool.Parse(modal.AutoAssignRoles_OnVoiceChannelJoin);
            guild.Settings.AssignPrivateMeetingRole_OnVoiceChannelJoin = bool.Parse(modal.AssignPrivateMeetingRole_OnVoiceChannelJoin);
            guild.Settings.AutoChangeNicknames = bool.Parse(modal.AutoChangeNicknames);
            guild.Settings.AssignArtccStaffRole = bool.Parse(modal.AssignArtccStaffRole);
        }
        catch (FormatException ex)
        {
            _logger.LogDebug("Config: " + ex.Message);
            await FollowupAsync("Could not parse one or more of your settings. Please try again.", ephemeral: true);
            return;
        }
        catch (ArgumentNullException ex)
        {
            _logger.LogDebug("Config: " + ex.Message);
            await FollowupAsync("Could not parse one or more of your settings. Please try again.", ephemeral: true);
            return;
        }

        await _guildData.UpdateGuild(guild);

        await FollowupAsync("Configuration for Discord events have been updated.", ephemeral: true);
    }

    [ModalInteraction("configModal_RoleNames")]
    public async Task ChangeRoleNamesConfig(ConfigModal_RoleNames modal)
    {
        await DeferAsync(ephemeral: true);
        GuildModel guild = await _guildData.GetGuildAsync(Context.Guild.Id);

        try
        {
            guild.Settings.PrivateMeetingRole = Context.Guild.Roles.First(x => x.Name == modal.PrivateMeetingRole).Name;
            guild.Settings.VerifiedRoleName = Context.Guild.Roles.First(x => x.Name == modal.VerifiedRoleName).Name;
            guild.Settings.ArtccStaffRoleName = Context.Guild.Roles.First(x => x.Name == modal.ArtccStaffRoleName).Name;
        }
        catch (Exception ex)
        {
            _logger.LogDebug("Config: " + ex.Message);
            await FollowupAsync("One or more of the roles you defined could not be found in your discord. \n" +
                "Please verify these roles exist. Note: These are case sensitive.", ephemeral: true);
            return;
        }

        await _guildData.UpdateGuild(guild);

        await FollowupAsync("Configuration for Discord Roles have been updated.", ephemeral: true);
    }

    [ModalInteraction("configModal_ChannelNames")]
    public async Task ChangeChannelNamesConfig(ConfigModal_ChannelNames modal)
    {
        await DeferAsync(ephemeral: true);
        await FollowupAsync("Nothing really changed, this functionality is still under development...", ephemeral: true);
    }
}
