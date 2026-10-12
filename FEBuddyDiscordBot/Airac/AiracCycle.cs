namespace FEBuddyDiscordBot.Airac;

/// <summary>
/// An AIRAC cycle: a 28-day period starting on its effective date at 0000Z, named "YYNN" (year of the effective date,
/// then the cycle's number within that year, e.g. 2611). Same math as FE-BUDDY's AiracCycleResolver.
/// </summary>
public sealed record AiracCycle(string Id, DateOnly EffectiveDate)
{
    public const int DaysPerCycle = 28;

    /// <summary>The cycle every other is counted from: 2601, effective 2026-01-22.</summary>
    private static readonly DateOnly ReferenceEffectiveDate = new(2026, 1, 22);

    /// <summary>The cycle in effect on <paramref name="date"/> (a UTC date: cycles change at 0000Z).</summary>
    public static AiracCycle Current(DateOnly date)
    {
        int cyclesSinceReference = (int)Math.Floor((date.DayNumber - ReferenceEffectiveDate.DayNumber) / (double)DaysPerCycle);
        return ForEffectiveDate(ReferenceEffectiveDate.AddDays(cyclesSinceReference * DaysPerCycle));
    }

    /// <summary>The cycle after the one in effect on <paramref name="date"/>.</summary>
    public static AiracCycle Next(DateOnly date) => ForEffectiveDate(Current(date).EffectiveDate.AddDays(DaysPerCycle));

    // Some years have 14 cycles (e.g. 2014, effective 2020-12-31).
    private static AiracCycle ForEffectiveDate(DateOnly effectiveDate)
    {
        int numberInYear = (effectiveDate.DayOfYear - 1) / DaysPerCycle + 1;
        return new($"{effectiveDate.Year % 100:00}{numberInYear:00}", effectiveDate);
    }
}
