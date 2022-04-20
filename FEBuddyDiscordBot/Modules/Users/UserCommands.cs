using FEBuddyDiscordBot.DataAccess.DB;
using FEBuddyDiscordBot.Models;
using FEBuddyDiscordBot.Services;

namespace FEBuddyDiscordBot.Modules.Users;

[Name("User Commands")]
[Summary("Helpful commands for All users in the Discord server!")]
public class UserCommands : ModuleBase
{
    private readonly IServiceProvider _services;
    private readonly IConfiguration _config;
    private readonly DiscordSocketClient _discord;
    private readonly ILogger _logger;
    private readonly IMongoGuildData _guildData;

    public UserCommands(IServiceProvider services)
    {
        _services = services;
        _config = _services.GetRequiredService<IConfiguration>();
        _discord = _services.GetRequiredService<DiscordSocketClient>();
        _logger = _services.GetRequiredService<ILogger<UserCommands>>();
        _guildData = _services.GetRequiredService<IMongoGuildData>();

        _logger.LogDebug("Module: Loaded UserCommands");
    }

    // Discord Server User commands go here.

    [Command("give-roles", RunMode = RunMode.Async), Alias(new string[] {"gr", "give-role", "assign-roles", "assign-role" }), Name("Give-Roles"), Summary("Give Discord Server Roles Depending on VATUSA Status.")]
    public async Task AssignRoles()
    {
        if (Context.Channel is IGuildChannel)
        {
            await Context.Message.DeleteAsync();
            GuildModel guild = await _guildData.GetGuildAsync(Context.Guild.Id);
            EmbedBuilder embed = await _services.GetRequiredService<RoleAssignmentService>().GiveRole((SocketGuildUser)Context.User, guild);
            await ReplyAsync(embed: embed.Build());
        }
        else
        {
            await ReplyAsync("Listen... The messages you send me will always remain between just you and I... This is because I am your buddy, and it is important that friends keep secrets... but mostly because I'm not real and nobody is monitoring these messages.\n\n(private message an admin of the discord to get appropriate roles assigned or... (VATUSA stuff))");
        }
    }
}
