using FEBuddyDiscordBot.DataAccess;
using FEBuddyDiscordBot.Models;

namespace FEBuddyDiscordBot.Modules.SlashCommands;

/// <summary>
/// Bot configuration for members with Manage Server (or the bot owner).
/// Every option is optional: only the options you fill in are changed.
/// </summary>
[Group("admin", "Bot configuration (requires Manage Server)")]
[DefaultMemberPermissions(GuildPermission.ManageGuild)]
[RequireUserPermission(GuildPermission.ManageGuild, Group = "AdminPermission")]
[RequireOwner(Group = "AdminPermission")]
public sealed class AdminSlashCommands : InteractionModuleBase<SocketInteractionContext>
{
    private readonly GuildSettingsStore _settings;
    private readonly ILogger<AdminSlashCommands> _logger;

    public AdminSlashCommands(GuildSettingsStore settings, ILogger<AdminSlashCommands> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    [SlashCommand("settings", "Show the bot's current configuration.")]
    public async Task ShowSettingsAsync()
    {
        await RespondAsync(embed: BuildSettingsEmbed(_settings.Current), ephemeral: true);
    }

    [SlashCommand("events", "Turn the bot's automatic behaviors on or off.")]
    public async Task SetEventsAsync(
        [Summary("assign-on-join", "Assign roles when someone joins the server")] bool? assignOnJoin = null,
        [Summary("assign-on-voice-join", "Assign roles when someone connects to voice")] bool? assignOnVoiceJoin = null,
        [Summary("private-meeting-role", "Give the private meeting role while someone is in the private meeting channel")] bool? privateMeetingRole = null,
        [Summary("change-nicknames", "Set nicknames to \"First Last | ARTCC\"")] bool? changeNicknames = null,
        [Summary("assign-staff-role", "Give the staff role to ARTCC staff (ATM, DATM, TA, EC, FE, WM)")] bool? assignStaffRole = null)
    {
        GuildSettings updated = await _settings.UpdateAsync(s =>
        {
            if (assignOnJoin is bool a) s.AssignRolesOnJoin = a;
            if (assignOnVoiceJoin is bool b) s.AssignRolesOnVoiceJoin = b;
            if (privateMeetingRole is bool c) s.PrivateMeetingRoleEnabled = c;
            if (changeNicknames is bool d) s.ChangeNicknames = d;
            if (assignStaffRole is bool e) s.AssignStaffRole = e;
        });

        _logger.LogInformation("Config: events updated by {User}", Context.User.Username);
        await RespondAsync("Events updated.", embed: BuildSettingsEmbed(updated), ephemeral: true);
    }

    [SlashCommand("roles", "Choose which roles the bot assigns.")]
    public async Task SetRolesAsync(
        [Summary("verified", "Role for members linked on VATUSA")] IRole? verified = null,
        [Summary("staff", "Role for ARTCC staff")] IRole? staff = null,
        [Summary("private-meeting", "Role given while in the private meeting voice channel")] IRole? privateMeeting = null)
    {
        List<string> warnings = [];
        foreach (IRole? role in new[] { verified, staff, privateMeeting })
        {
            if (role is not null && CantAssign(role) is string problem) warnings.Add(problem);
        }

        GuildSettings updated = await _settings.UpdateAsync(s =>
        {
            if (verified is not null) s.VerifiedRoleId = verified.Id;
            if (staff is not null) s.StaffRoleId = staff.Id;
            if (privateMeeting is not null) s.PrivateMeetingRoleId = privateMeeting.Id;
        });

        _logger.LogInformation("Config: roles updated by {User}", Context.User.Username);

        string message = warnings.Count == 0 ? "Roles updated." : "Roles updated, but:\n" + string.Join("\n", warnings.Select(w => $"⚠️ {w}"));
        await RespondAsync(message, embed: BuildSettingsEmbed(updated), ephemeral: true);
    }

    [SlashCommand("channels", "Choose the channels the bot uses.")]
    public async Task SetChannelsAsync(
        [Summary("private-meeting", "Voice channel that grants the private meeting role"), ChannelTypes(ChannelType.Voice, ChannelType.Stage)] IVoiceChannel? privateMeeting = null,
        [Summary("roles-channel", "Channel members are pointed to for /give-role"), ChannelTypes(ChannelType.Text)] ITextChannel? rolesChannel = null)
    {
        GuildSettings updated = await _settings.UpdateAsync(s =>
        {
            if (privateMeeting is not null) s.PrivateMeetingChannelId = privateMeeting.Id;
            if (rolesChannel is not null) s.RolesChannelId = rolesChannel.Id;
        });

        _logger.LogInformation("Config: channels updated by {User}", Context.User.Username);
        await RespondAsync("Channels updated.", embed: BuildSettingsEmbed(updated), ephemeral: true);
    }

    /// <summary>Returns why the bot can't assign this role, or null if it can.</summary>
    private string? CantAssign(IRole role)
    {
        if (role.Id == Context.Guild.EveryoneRole.Id) return "@everyone can't be assigned.";
        if (role.IsManaged) return $"{role.Mention} is managed by an integration and can't be assigned.";
        if (!Context.Guild.CurrentUser.GuildPermissions.ManageRoles) return "I don't have the Manage Roles permission.";
        if (role.Position >= Context.Guild.CurrentUser.Hierarchy) return $"My role must be above {role.Mention} in Server Settings → Roles.";
        return null;
    }

    private Embed BuildSettingsEmbed(GuildSettings s)
    {
        static string OnOff(bool value) => value ? "✅ On" : "⬜ Off";

        string Role(ulong? id) => id is ulong roleId
            ? Context.Guild.GetRole(roleId)?.Mention ?? "⚠️ deleted role"
            : "⚠️ not set";

        string Channel(ulong? id) => id is ulong channelId
            ? Context.Guild.GetChannel(channelId) is { } channel ? $"<#{channel.Id}>" : "⚠️ deleted channel"
            : "⚠️ not set";

        return new EmbedBuilder()
            .WithTitle($"FE-Buddy bot settings for {Context.Guild.Name}")
            .WithColor(Color.Blue)
            .AddField("Events",
                $"Assign roles on join: {OnOff(s.AssignRolesOnJoin)}\n" +
                $"Assign roles on voice join: {OnOff(s.AssignRolesOnVoiceJoin)}\n" +
                $"Private meeting role: {OnOff(s.PrivateMeetingRoleEnabled)}\n" +
                $"Change nicknames: {OnOff(s.ChangeNicknames)}\n" +
                $"Assign staff role: {OnOff(s.AssignStaffRole)}")
            .AddField("Roles",
                $"Verified: {Role(s.VerifiedRoleId)}\n" +
                $"Staff: {Role(s.StaffRoleId)}\n" +
                $"Private meeting: {Role(s.PrivateMeetingRoleId)}")
            .AddField("Channels",
                $"Private meeting voice: {Channel(s.PrivateMeetingChannelId)}\n" +
                $"Roles channel: {Channel(s.RolesChannelId)}")
            .WithFooter("Change with /admin events, /admin roles, /admin channels")
            .Build();
    }
}
