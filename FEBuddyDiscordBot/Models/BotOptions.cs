namespace FEBuddyDiscordBot.Models;

/// <summary>
/// Core bot settings, bound from the "Bot" configuration section.
/// Secrets (Token, DisconnectWebhookUrl) should come from user-secrets or environment variables, never appsettings.json.
/// </summary>
public sealed class BotOptions
{
    public const string SectionName = "Bot";

    /// <summary>Discord bot token. Environment variable: Bot__Token</summary>
    public string Token { get; set; } = "";

    /// <summary>The one Discord server (guild) this bot serves.</summary>
    public ulong GuildId { get; set; }

    /// <summary>Optional custom status shown under the bot's name.</summary>
    public string? Status { get; set; }

    /// <summary>Optional Discord webhook that is notified when the bot restarts after losing its connection.</summary>
    public string? DisconnectWebhookUrl { get; set; }

    /// <summary>
    /// Ask Discord for message text (the privileged Message Content intent), needed to copy every reply in issue posts
    /// to GitHub. Turn it on in the Developer Portal (Bot → Message Content Intent) FIRST: if the portal doesn't allow
    /// it, Discord refuses the connection and the bot can't start.
    /// </summary>
    public bool MessageContentIntent { get; set; }

    /// <summary>How long the bot may stay disconnected from Discord before the app exits so Docker can restart it.</summary>
    public int DisconnectTimeoutSeconds { get; set; } = 60;

    /// <summary>Folder that holds the SQLite database. Relative paths are resolved from the app's content root.</summary>
    public string DataDirectory { get; set; } = "data";
}

/// <summary>
/// Uptime Kuma "push" monitor settings, bound from the "Heartbeat" configuration section.
/// </summary>
public sealed class HeartbeatOptions
{
    public const string SectionName = "Heartbeat";

    public bool Enabled { get; set; }

    /// <summary>Push URL without a query string, e.g. http://host:3001/api/push/abc123</summary>
    public string? Url { get; set; }

    public int IntervalSeconds { get; set; } = 60;
}

/// <summary>
/// Settings used to seed the database the first time the bot starts with an empty database.
/// Roles and channels are given by name here and resolved to IDs against the server at that time.
/// After seeding, use /admin to change settings; these values are no longer read.
/// </summary>
public sealed class GuildDefaultsOptions
{
    public const string SectionName = "GuildDefaults";

    public bool AssignRolesOnJoin { get; set; }
    public bool AssignRolesOnVoiceJoin { get; set; }
    public bool PrivateMeetingRoleEnabled { get; set; }
    public bool ChangeNicknames { get; set; }
    public bool AssignStaffRole { get; set; }

    public string? VerifiedRoleName { get; set; }
    public string? StaffRoleName { get; set; }
    public string? PrivateMeetingRoleName { get; set; }
    public string? PrivateMeetingChannelName { get; set; }
    public string? RolesChannelName { get; set; }
}
