using Discord.Interactions;
using FEBuddyDiscordBot.DataAccess.DB;
using FEBuddyDiscordBot.Models;
using FEBuddyDiscordBot.Services;

namespace FEBuddyDiscordBot.Modules.SlashCommands;

/// <summary>
/// User Discord Slash Commands and Interactions
/// </summary>
public class UserSlashCommands : InteractionModuleBase<SocketInteractionContext>
{
    // Dependency Injection Services Required
    private readonly IServiceProvider _services;
    private readonly IConfiguration _config;
    private readonly DiscordSocketClient _discord;
    private readonly ILogger _logger;
    private readonly IMongoGuildData _guildData;

    /// <summary>
    /// Initialize the User Slash Commands Module (This might be unnecessary)
    /// </summary>
    /// <param name="services">Dependency Injection Service Provider</param>
    public UserSlashCommands(IServiceProvider services)
    {
        _services = services;
        _config = _services.GetRequiredService<IConfiguration>();
        _discord = _services.GetRequiredService<DiscordSocketClient>();
        _logger = _services.GetRequiredService<ILogger<UserSlashCommands>>();
        _guildData = _services.GetRequiredService<IMongoGuildData>();

        _logger.LogDebug("Module: Loaded UserSlashCommands");
    }

    /// <summary>
    /// Slash Command for Giving Roles/Permissions to the user performing the command.
    /// </summary>
    /// <returns>None</returns>
    [SlashCommand("give-role", "Get discord roles/permissions. Your Discord account must be linked on the VATUSA website.")]
    public async Task AssignRoles()
    {
        //await DeferAsync(ephemeral: true); // ephemeral means that only the person doing the command will see the message/response.
        await DeferAsync();
        GuildModel guild = await _guildData.GetGuildAsync(Context.Guild.Id);
        var embed = await _services.GetRequiredService<RoleAssignmentService>().GiveRole((SocketGuildUser)Context.User, guild);
        await FollowupAsync(embed: embed.Build());
    }
}
