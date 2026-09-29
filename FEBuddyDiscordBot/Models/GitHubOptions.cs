namespace FEBuddyDiscordBot.Models;

/// <summary>
/// GitHub App used to create and follow FE-BUDDY issues, bound from the "GitHub" configuration section.
/// Issue features stay off until <see cref="AppId"/> and <see cref="PrivateKeyPath"/> are set.
/// </summary>
public sealed class GitHubOptions
{
    public const string SectionName = "GitHub";

    /// <summary>The GitHub App's "App ID" (from the app's settings page).</summary>
    public long AppId { get; set; }

    /// <summary>
    /// Path to the app's private key (.pem). Relative paths are resolved from the app's content root.
    /// The key itself is a secret; keep the file readable only by the bot.
    /// </summary>
    public string? PrivateKeyPath { get; set; }

    /// <summary>The repository issues are created in, as "owner/name".</summary>
    public string Repository { get; set; } = "Nikolai558/FE-BUDDY";

    /// <summary>Where the version dropdown's releases come from. Stays FE-BUDDY even when testing against a sandbox.</summary>
    public string ReleasesRepository { get; set; } = "Nikolai558/FE-BUDDY";

    /// <summary>How often to check GitHub for new issues.</summary>
    public int PollIntervalSeconds { get; set; } = 120;

    public bool IsConfigured => AppId > 0 && !string.IsNullOrWhiteSpace(PrivateKeyPath);
}
