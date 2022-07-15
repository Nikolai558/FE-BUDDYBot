using FEBuddyDiscordBot.DataAccess.DB;
using FEBuddyDiscordBot.Models;
using FEBuddyDiscordBot.Services;
using System.Text;

namespace FEBuddyDiscordBot.Modules.Staff;

/// <summary>
/// Discord Staff Only Commands
/// </summary>
[Name("Staff Commands")]
[Summary("These commands are to assist the Staff of the server.")]
[RequireUserPermission(GuildPermission.ManageMessages | GuildPermission.ManageChannels)]
public class StaffCommands : ModuleBase
{
    // Dependency Injection services Required
    private readonly IServiceProvider _services;
    private readonly IConfiguration _config;
    private readonly DiscordSocketClient _discord;
    private readonly ILogger _logger;

    /// <summary>
    /// Initialize the Staff Commands Module (This might be unnecessary)
    /// </summary>
    /// <param name="services">Dependency Injection Service Provider</param>
    public StaffCommands(IServiceProvider services)
    {
        _services = services;
        _config = _services.GetRequiredService<IConfiguration>();
        _discord = _services.GetRequiredService<DiscordSocketClient>();
        _logger = _services.GetRequiredService<ILogger<StaffCommands>>();

        _logger.LogDebug("Module: Loaded StaffCommands");
    }

    // Discord Server Staff commands go here.
    [Command("check-users", RunMode = RunMode.Async), Name("Check-Users"), Summary("Go through all users in the Guild and check their verification status and nickname"), Alias("verify-check")]
    public async Task ServerCount()
    {
        await Context.Message.Author.SendMessageAsync("This command is a WIP (Work in Progress)");

        var roleAssignment = _services.GetRequiredService<RoleAssignmentService>();
        GuildModel guild = await _services.GetRequiredService<IMongoGuildData>().GetGuildAsync(Context.Guild.Id);
        List<IReadOnlyCollection<IUser>> users = await Context.Channel.GetUsersAsync(CacheMode.AllowDownload).ToListAsync();
        var originalMSG = Context.Message;

        await Context.Message.DeleteAsync();

        var invalidOpUsers = new StringBuilder();

        foreach (IReadOnlyCollection<IUser> userCollection in users)
        {
            foreach (IUser user in userCollection)
            {
                try
                {
                    if (user.IsBot)
                    {
                        continue;
                    }
                    await roleAssignment.GiveRole((SocketGuildUser)user, guild, false);
                }
                catch (Exception)
                {
                    invalidOpUsers.AppendLine("    " + user.Username);
                }
            }
        }

        await originalMSG.Author.SendMessageAsync("Check Users command Completed..\nUnable to check the following users:\n" +invalidOpUsers.ToString());
    }
}
