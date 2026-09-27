namespace FEBuddyDiscordBot.Modules.SlashCommands;

/// <summary>
/// Commands only the bot's owner (the Discord application owner) can run.
/// </summary>
[Group("owner", "Bot owner commands")]
[DefaultMemberPermissions(GuildPermission.Administrator)]
[RequireOwner]
public sealed class OwnerSlashCommands : InteractionModuleBase<SocketInteractionContext>
{
    [SlashCommand("set-status", "Set the status text shown under the bot's name (until the bot restarts).")]
    public async Task SetStatusAsync([Summary("text", "Status text. Leave empty to clear it.")] string? text = null)
    {
        await Context.Client.SetCustomStatusAsync(text ?? "");
        await RespondAsync(string.IsNullOrWhiteSpace(text) ? "Status cleared." : $"Status set to: {text}", ephemeral: true);
    }
}
