using Discord.Interactions;
using FEBuddyDiscordBot.DataAccess.DB;
using FEBuddyDiscordBot.Models;
using FEBuddyDiscordBot.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FEBuddyDiscordBot.Modules.SlashCommands;

public class UserSlashCommands: InteractionModuleBase<SocketInteractionContext>
{

    private readonly IServiceProvider _services;
    private readonly IConfiguration _config;
    private readonly DiscordSocketClient _discord;
    private readonly ILogger _logger;
    private readonly IMongoGuildData _guildData;

    public UserSlashCommands(IServiceProvider services)
    {
        _services = services;
        _config = _services.GetRequiredService<IConfiguration>();
        _discord = _services.GetRequiredService<DiscordSocketClient>();
        _logger = _services.GetRequiredService<ILogger<UserSlashCommands>>();
        _guildData = _services.GetRequiredService<IMongoGuildData>();

        _logger.LogDebug("Module: Loaded UserSlashCommands");
    }

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
