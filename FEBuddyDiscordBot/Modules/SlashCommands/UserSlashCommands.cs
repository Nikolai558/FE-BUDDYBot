using FEBuddyDiscordBot.Services;

namespace FEBuddyDiscordBot.Modules.SlashCommands;

/// <summary>
/// Commands for everyone in the server.
/// </summary>
public sealed class UserSlashCommands : InteractionModuleBase<SocketInteractionContext>
{
    private readonly RoleAssignmentService _roleAssignment;

    public UserSlashCommands(RoleAssignmentService roleAssignment)
    {
        _roleAssignment = roleAssignment;
    }

    [SlashCommand("give-role", "Get your server roles and nickname. Your Discord account must be linked on the VATUSA website.")]
    public async Task GiveRoleAsync()
    {
        await DeferAsync(ephemeral: true);
        RoleAssignmentResult result = await _roleAssignment.AssignRolesAsync((SocketGuildUser)Context.User, notifyIfNotLinked: false);
        await FollowupAsync(embed: result.ToEmbed(), ephemeral: true);
    }
}
