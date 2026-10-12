using System.Globalization;

namespace FEBuddyDiscordBot.Airac;

/// <summary>
/// When the bot looks for the next cycle's d-TPP Metafile: a list of UTC times, every day, set with /admin airac-check.
/// </summary>
public static class MetafileCheckTimes
{
    /// <summary>Enough for a check every 30 minutes, and short enough to fit in the /admin settings embed.</summary>
    public const int MaxTimes = 48;

    /// <summary>Until changed: 11:00 and 17:00 US Eastern daylight time.</summary>
    public static TimeOnly[] Default => [new(15, 0), new(21, 0)];

    /// <summary>The first check time after <paramref name="after"/>, or null if there are no check times.</summary>
    public static DateTimeOffset? NextAfter(DateTimeOffset after, IReadOnlyCollection<TimeOnly> times)
    {
        if (times.Count == 0) return null;

        DateOnly today = DateOnly.FromDateTime(after.UtcDateTime);
        return new[] { today, today.AddDays(1) }
            .SelectMany(day => times.Select(time => new DateTimeOffset(day.ToDateTime(time), TimeSpan.Zero)))
            .Where(check => check > after)
            .Min();
    }

    /// <summary>"1500z, 2100z", or "off" when there are none.</summary>
    public static string Format(IEnumerable<TimeOnly> times)
    {
        string[] formatted = times.Select(t => t.ToString("HHmm", CultureInfo.InvariantCulture) + "z").ToArray();
        return formatted.Length == 0 ? "off" : string.Join(", ", formatted);
    }

    /// <summary>
    /// Read a list of UTC times like "1200z, 2300z" (also "1200", "12:00z", or separated by spaces), sorted with
    /// duplicates removed. "off" means no checks. Returns false, with <paramref name="error"/> set, if any part isn't a time.
    /// </summary>
    public static bool TryParse(string input, out TimeOnly[] times, out string? error)
    {
        times = [];
        error = null;

        string[] parts = input.Split([',', ';', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts is [var only] && only.Equals("off", StringComparison.OrdinalIgnoreCase)) return true;

        if (parts.Length == 0)
        {
            error = "Give at least one time, like `1200z`, or `off` to stop checking.";
            return false;
        }

        SortedSet<TimeOnly> parsed = [];
        foreach (string part in parts)
        {
            string digits = part.TrimEnd('z', 'Z').Replace(":", "");
            if (digits.Length != 4 || !digits.All(char.IsAsciiDigit)
                || !TimeOnly.TryParseExact(digits, "HHmm", CultureInfo.InvariantCulture, DateTimeStyles.None, out TimeOnly time))
            {
                error = $"`{part}` isn't a UTC time. Use four digits from `0000z` to `2359z`, like `1200z`.";
                return false;
            }

            parsed.Add(time);
        }

        if (parsed.Count > MaxTimes)
        {
            error = $"That's {parsed.Count} times; the most is {MaxTimes} (every 30 minutes).";
            return false;
        }

        times = [.. parsed];
        return true;
    }
}
