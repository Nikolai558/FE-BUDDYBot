using FEBuddyDiscordBot.Issues;
using FEBuddyDiscordBot.Services;

namespace FEBuddyDiscordBot.Modules.SlashCommands;

/// <summary>
/// /report: the same flow as the buttons in the submit channel.
/// </summary>
public sealed class IssueSlashCommands : InteractionModuleBase<SocketInteractionContext>
{
    private readonly IssueInteractionHandler _issues;

    public IssueSlashCommands(IssueInteractionHandler issues)
    {
        _issues = issues;
    }

    [SlashCommand("report", "Report an FE-BUDDY bug, request a feature, or flag a docs problem. Needs the Verified role.")]
    public async Task ReportAsync([Summary("type", "What are you reporting?")] IssueKind type)
    {
        await _issues.StartAsync(Context.Interaction, type);
    }
}
