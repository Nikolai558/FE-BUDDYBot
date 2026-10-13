namespace FEBuddyDiscordBot.Announcements;

/// <summary>How finished an FE-BUDDY release is, least to most. Announcing a level also announces every level above it.</summary>
public enum ReleaseLevel
{
    [ChoiceDisplay("Alpha (alpha, beta, RC and stable)")]
    Alpha,

    [ChoiceDisplay("Beta (beta, RC and stable)")]
    Beta,

    [ChoiceDisplay("RC (release candidates and stable)")]
    ReleaseCandidate,

    [ChoiceDisplay("Stable only")]
    Stable,
}

public static class ReleaseLevels
{
    /// <summary>
    /// The level of a release from its tag's pre-release part: "3.0.0-beta.10" is Beta, "3.0.0-rc.1" is RC, "2.9.3" is Stable.
    /// Anything else marked as a pre-release on GitHub counts as Alpha, so it's only announced to the earliest testers.
    /// </summary>
    public static ReleaseLevel FromTag(string tag, bool prerelease)
    {
        string version = tag.TrimStart('v', 'V');
        int dash = version.IndexOf('-');
        if (dash < 0) return prerelease ? ReleaseLevel.Alpha : ReleaseLevel.Stable;

        string suffix = version[(dash + 1)..];
        if (suffix.StartsWith("rc", StringComparison.OrdinalIgnoreCase)) return ReleaseLevel.ReleaseCandidate;
        if (suffix.StartsWith("beta", StringComparison.OrdinalIgnoreCase)) return ReleaseLevel.Beta;
        return ReleaseLevel.Alpha;
    }

    public static string Name(ReleaseLevel level) => level switch
    {
        ReleaseLevel.Alpha => "Alpha",
        ReleaseLevel.Beta => "Beta",
        ReleaseLevel.ReleaseCandidate => "Release candidate",
        _ => "Stable",
    };
}
