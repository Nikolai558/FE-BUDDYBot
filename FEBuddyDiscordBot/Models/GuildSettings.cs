namespace FEBuddyDiscordBot.Models;

/// <summary>
/// Runtime-editable settings for the Discord server, stored in SQLite and changed with the /admin commands.
/// Roles and channels are stored by ID so renaming them in Discord does not break the bot.
/// </summary>
public sealed record GuildSettings
{
    /// <summary>Settings used before the database has been seeded: everything off.</summary>
    public static GuildSettings Disabled { get; } = new();

    public bool AssignRolesOnJoin { get; set; }
    public bool AssignRolesOnVoiceJoin { get; set; }
    public bool PrivateMeetingRoleEnabled { get; set; }
    public bool ChangeNicknames { get; set; }
    public bool AssignStaffRole { get; set; }

    public ulong? VerifiedRoleId { get; set; }
    public ulong? StaffRoleId { get; set; }
    public ulong? PrivateMeetingRoleId { get; set; }
    public ulong? PrivateMeetingChannelId { get; set; }
    public ulong? RolesChannelId { get; set; }
}
