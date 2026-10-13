using Discord.Net;
using FEBuddyDiscordBot.Announcements;
using FEBuddyDiscordBot.DataAccess;

namespace FEBuddyDiscordBot.Modules.SlashCommands;

/// <summary>
/// The button under each posted release announcement: members who have the release ping role lose it, and members who
/// don't get it. Nobody gets the role automatically.
/// </summary>
public sealed class ReleasePingCommands : InteractionModuleBase<SocketInteractionContext>
{
    private readonly GuildSettingsStore _settings;
    private readonly ILogger<ReleasePingCommands> _logger;

    public ReleasePingCommands(GuildSettingsStore settings, ILogger<ReleasePingCommands> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    [ComponentInteraction(ReleasePreview.PingToggleButtonId, ignoreGroupNames: true)]
    public async Task ToggleAsync()
    {
        SocketGuildUser user = (SocketGuildUser)Context.User;
        if (_settings.Current.ReleasePingRoleId is not ulong roleId || user.Guild.GetRole(roleId) is not { } role)
        {
            await RespondAsync("Release notifications aren't set up on this server any more.", ephemeral: true);
            return;
        }

        bool on = user.Roles.All(r => r.Id != role.Id);
        try
        {
            if (on) await user.AddRoleAsync(role);
            else await user.RemoveRoleAsync(role);
        }
        catch (HttpException ex) when (ex.DiscordCode == DiscordErrorCode.MissingPermissions)
        {
            _logger.LogWarning("Missing Permissions: could not {Action} {Role} for {User} ({UserId}). The bot's role must be above {Role}.",
                on ? "give" : "remove", role.Name, user.Username, user.Id, role.Name);
            await RespondAsync($"I couldn't {(on ? "give you" : "remove")} {role.Mention}. Please let an admin know.", ephemeral: true, allowedMentions: AllowedMentions.None);
            return;
        }

        _logger.LogInformation("Releases: {User} ({UserId}) turned release notifications {State}", user.Username, user.Id, on ? "on" : "off");
        await RespondAsync(on
                ? $"You'll be pinged ({role.Mention}) when a new FE-BUDDY release is announced. Press the button again to stop."
                : $"You won't be pinged for new FE-BUDDY releases any more. Press the button again to turn it back on.",
            ephemeral: true, allowedMentions: AllowedMentions.None);
    }
}
