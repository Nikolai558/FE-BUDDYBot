namespace FEBuddyDiscordBot.Services;

/// <summary>The result of a background job's latest run.</summary>
public sealed record JobRun(DateTimeOffset AtUtc, bool Ok, string Detail);

/// <summary>
/// How the background jobs last went, for /owner status. Kept in memory only, so it starts empty after a restart.
/// </summary>
public sealed class BotHealth
{
    public DateTimeOffset StartedUtc { get; } = DateTimeOffset.UtcNow;

    public JobRun? AiracCheck { get; private set; }
    public JobRun? IssueSync { get; private set; }
    public JobRun? ReleaseCheck { get; private set; }

    public void RecordAiracCheck(bool ok, string detail) => AiracCheck = new(DateTimeOffset.UtcNow, ok, detail);
    public void RecordIssueSync(bool ok, string detail) => IssueSync = new(DateTimeOffset.UtcNow, ok, detail);
    public void RecordReleaseCheck(bool ok, string detail) => ReleaseCheck = new(DateTimeOffset.UtcNow, ok, detail);
}
