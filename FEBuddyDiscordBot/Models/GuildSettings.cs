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

    // GitHub issues (see Docs/GitHub Issue Sync.md)

    /// <summary>Forum channel with one post per open FE-BUDDY issue.</summary>
    public ulong? IssueForumChannelId { get; set; }

    /// <summary>Channel with the "report an issue" buttons.</summary>
    public ulong? IssueSubmitChannelId { get; set; }

    /// <summary>Private channel where submissions over the hourly limit wait for approval.</summary>
    public ulong? IssueApprovalChannelId { get; set; }

    /// <summary>Role pinged for approvals. Members with it can also approve, as can anyone with Manage Server.</summary>
    public ulong? IssueApproverRoleId { get; set; }

    /// <summary>Roles allowed to submit development tasks.</summary>
    public ulong[]? DevTaskRoleIds { get; set; }

    /// <summary>Submissions a member may make per hour before the rest need approval.</summary>
    public int IssueSubmissionsPerHour { get; set; } = 1;
}
