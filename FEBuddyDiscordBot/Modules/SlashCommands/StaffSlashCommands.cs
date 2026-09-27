using System.Text;
using FEBuddyDiscordBot.Services;

namespace FEBuddyDiscordBot.Modules.SlashCommands;

/// <summary>
/// Commands for server staff.
/// </summary>
[Group("staff", "Server staff commands")]
[DefaultMemberPermissions(GuildPermission.ManageMessages | GuildPermission.ManageChannels)]
[RequireUserPermission(GuildPermission.ManageMessages | GuildPermission.ManageChannels)]
public sealed class StaffSlashCommands : InteractionModuleBase<SocketInteractionContext>
{
    // Only one member check may run at a time.
    private static readonly SemaphoreSlim CheckUsersLock = new(1, 1);

    private readonly RoleAssignmentService _roleAssignment;
    private readonly ILogger<StaffSlashCommands> _logger;

    public StaffSlashCommands(RoleAssignmentService roleAssignment, ILogger<StaffSlashCommands> logger)
    {
        _roleAssignment = roleAssignment;
        _logger = logger;
    }

    [SlashCommand("check-users", "Re-check every member's VATUSA link, roles, and nickname.")]
    public async Task CheckUsersAsync()
    {
        if (!await CheckUsersLock.WaitAsync(0))
        {
            await RespondAsync("A member check is already running. Please wait for it to finish.", ephemeral: true);
            return;
        }

        try
        {
            await DeferAsync(ephemeral: true);

            List<SocketGuildUser> members = Context.Guild.Users.Where(u => !u.IsBot).ToList();
            int assigned = 0, notLinked = 0;
            List<string> failed = [];

            _logger.LogInformation("Check Users: checking {Count} members", members.Count);

            foreach (SocketGuildUser member in members)
            {
                try
                {
                    RoleAssignmentResult result = await _roleAssignment.AssignRolesAsync(member, notifyIfNotLinked: false);
                    switch (result.Outcome)
                    {
                        case RoleAssignmentOutcome.Assigned: assigned++; break;
                        case RoleAssignmentOutcome.NotLinked: notLinked++; break;
                        default: failed.Add(member.DisplayName); break;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("Check Users: failed for {User} ({UserId}): {Error}", member.Username, member.Id, ex.Message);
                    failed.Add(member.DisplayName);
                }
            }

            StringBuilder summary = new($"Checked {members.Count} members: {assigned} linked on VATUSA, {notLinked} not linked.");
            if (failed.Count > 0) summary.Append($"\nCould not check {failed.Count}: {string.Join(", ", failed)}");

            string text = summary.Length > 1900 ? summary.ToString(0, 1900) + "…" : summary.ToString();

            try
            {
                await FollowupAsync(text, ephemeral: true);
            }
            catch (Exception)
            {
                // The interaction expires after 15 minutes; fall back to a DM for very large servers.
                await Context.User.SendMessageAsync(text);
            }
        }
        finally
        {
            CheckUsersLock.Release();
        }
    }
}
