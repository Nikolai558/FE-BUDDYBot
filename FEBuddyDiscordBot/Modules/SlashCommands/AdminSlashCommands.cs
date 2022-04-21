using Discord.Interactions;
using FEBuddyDiscordBot.DataAccess.DB;
using FEBuddyDiscordBot.Models;

namespace FEBuddyDiscordBot.Modules.SlashCommands;

/// <summary>
/// Admin Discord Slash Commands and Interactions
/// </summary>
[Discord.Interactions.Group("admin", "Discord Administrator Only Commands")]
[Discord.Interactions.RequireUserPermission(GuildPermission.Administrator, Group = "AdminPermission")]
[Discord.Interactions.RequireOwner(Group = "AdminPermission")]
public class AdminSlashCommands: InteractionModuleBase<SocketInteractionContext>
{
    // Dependency Injection Services Required
    private readonly IServiceProvider _services;
    private readonly IConfiguration _config;
    private readonly DiscordSocketClient _discord;
    private readonly ILogger _logger;
    private readonly IMongoGuildData _guildData;

    /// <summary>
    /// Initialize the Admin Slash Commands Module (This might be unnecessary)
    /// </summary>
    /// <param name="services">Dependency Injection Service Provider</param>
    public AdminSlashCommands(IServiceProvider services)
    {
        _services = services;
        _config = _services.GetRequiredService<IConfiguration>();
        _discord = _services.GetRequiredService<DiscordSocketClient>();
        _logger = _services.GetRequiredService<ILogger<AdminSlashCommands>>();
        _guildData = _services.GetRequiredService<IMongoGuildData>();

        _logger.LogDebug("Module: Loaded AdminSlashCommands");
    }
    
    [SlashCommand("config", "Setup/Modify configuration settings for this bot in this discord server.")]
    public async Task ConfigureServerSettings()
    {
        await DeferAsync(ephemeral: true);

        var configEmbed = await GetServerConfig(Context.Guild.Id);

        var menu = new SelectMenuBuilder()
        {
            CustomId = "configMenu",
            Placeholder = $"Change bot configuration for {Context.Guild.Name}"
        };

        menu.AddOption("Change Discord Events", "changeDiscordEvents");
        menu.AddOption("Change Role Names", "changeRoleNames");
        menu.AddOption("Change Channel Names", "changeChannelNames");

        var component = new ComponentBuilder()
            .WithSelectMenu(menu);
        
        await FollowupAsync(embed: configEmbed.Build(), components: component.Build(), ephemeral: true);
    }

    private async Task<EmbedBuilder> GetServerConfig(ulong serverId)
    {
        GuildModel guildinfo = await _guildData.GetGuildAsync(Context.Guild.Id);

        string discordEvents =
            $"Assign Roles on Join: \n\t`{guildinfo.Settings.AutoAssignRoles_OnJoin}`\n\n" +
            $"Assign Roles on VC Connect: \n\t`{guildinfo.Settings.AutoAssignRoles_OnVoiceChannelJoin}`\n\n" +
            $"Assign Private Meeting Role: \n\t`{guildinfo.Settings.AssignPrivateMeetingRole_OnVoiceChannelJoin}`\n\n" +
            $"Auto Change Nicknames: \n\t`{guildinfo.Settings.AutoChangeNicknames}`\n\n" +
            $"Assign ARTCC Staff Role: \n\t`{guildinfo.Settings.AssignArtccStaffRole}`\n\n\n";

        string discordRoles =
            $"Private Meeting Role Name: \n\t`{guildinfo.Settings.PrivateMeetingRole}`\n\n" +
            $"Verified Role Name: \n\t`{guildinfo.Settings.VerifiedRoleName}`\n\n" +
            $"ARTCC Staff Role Name: \n\t`{guildinfo.Settings.ArtccStaffRoleName}`\n\n";

        string discordChannels =
            $"Private Meeting Voice Channle Name: \n\t`{guildinfo.Settings.PrivateMeetingVoiceChannelName}`\n\n" +
            $"Assign Roles Text Channel Name: \n\t`{guildinfo.Settings.RolesTextChannelName}`\n\n";


        List<EmbedFieldBuilder> fields = new List<EmbedFieldBuilder>()
        {
            new EmbedFieldBuilder()
            {
                Name = "Discord Events",
                Value = discordEvents,
                IsInline = true
            },
            new EmbedFieldBuilder()
            {
                Name = "Roles",
                Value = discordRoles,
                IsInline= true
            },
            new EmbedFieldBuilder()
            {
                Name = "Channels",
                Value = discordChannels,
                IsInline = false
            }
        };

        EmbedBuilder output = new EmbedBuilder()
        {
            Title = $"Current Configuration for `{guildinfo.GuildName}`",
            Description = $"Command Prefix:`{guildinfo.Settings.Prefix}`\nDiscord events are commands that happen when an event is triggered. These values can be True or False." +
            $"Roles are the various roles that this bot can and will assign based on the different events that are set to True. Channels are the channels that this bot" +
            $"needs to know about to for the various events and commands to work properly if they are set to true. ",
            Fields = fields
        };

        return output;
    }
}


