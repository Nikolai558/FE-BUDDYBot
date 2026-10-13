using System.Text;
using System.Text.RegularExpressions;

namespace FEBuddyDiscordBot.Announcements;

/// <summary>
/// The first draft of a release announcement, built from the GitHub release: a title, the release's pre-release note,
/// its changelog (shortened to fit one Discord message), its install instructions and a link to the release.
/// FE-BUDDY's release notes have "## Instructions to install:" and "## Change log:" sections; anything else still works.
/// </summary>
public static partial class ReleaseAnnouncement
{
    /// <summary>Discord allows 2,000 characters per message; the rest is left for the ping.</summary>
    public const int MaxLength = 1900;

    /// <summary>The server emoji at the end of the title, if the server has it.</summary>
    public const string EmojiName = "FEB_cheer";

    private const int MaxNoteLength = 400;
    private const int MaxInstallLength = 700;
    private const int MaxShortBulletLength = 160;

    private const string DefaultInstall =
        "- **Already have FE-BUDDY:** click **Update available!** at the top of FE-BUDDY's window. It downloads and installs the update for you.\n" +
        "- **New install, or the update isn't working:** download the installer from the release below and run it.";

    public static string Build(string version, ReleaseLevel level, string releaseUrl, string? body, string? emoji)
    {
        Notes notes = Parse(body ?? "");

        string title = $"## FE-BUDDY {version} is released!{(emoji is null ? "" : " " + emoji)}\n";
        string note = notes.Preamble.Length > 0 ? Cap(notes.Preamble, MaxNoteLength) + "\n"
            : level != ReleaseLevel.Stable ? $"> **{ReleaseLevels.Name(level)} pre-release:** for testing, and it may have bugs.\n"
            : "";
        const string changelogHeading = "\n### Changelog\n";
        string install = "\n### How to update\n" + Cap(notes.Install ?? DefaultInstall, MaxInstallLength) + "\n";
        string link = $"\n[{(level == ReleaseLevel.Stable ? "Release" : "Pre-release")} {version} on GitHub](<{releaseUrl}>)";

        int budget = MaxLength - title.Length - note.Length - changelogHeading.Length - install.Length - link.Length;
        string changelog = FitChangelog(ChangelogLines(notes.Changelog), budget);

        return title + note + changelogHeading + changelog + "\n" + install + link;
    }

    private sealed record Notes(string Preamble, string? Install, string Changelog);

    /// <summary>Split release notes into the text before the first "## " heading, the install section and the changelog.</summary>
    private static Notes Parse(string body)
    {
        body = SuppressEmbeds(body.Replace("\r\n", "\n"));

        // The footer after the last "---" holds the compare link and checksums.
        int footer = body.LastIndexOf("\n---", StringComparison.Ordinal);
        if (footer >= 0) body = body[..footer];

        List<(string Heading, StringBuilder Text)> sections = [];
        StringBuilder preamble = new();
        foreach (string line in body.Split('\n'))
        {
            if (line.StartsWith("## ", StringComparison.Ordinal)) sections.Add((line[3..].Trim().TrimEnd(':'), new StringBuilder()));
            else (sections.Count > 0 ? sections[^1].Text : preamble).Append(line).Append('\n');
        }

        if (sections.Count == 0) return new("", null, preamble.ToString().Trim());

        string? install = sections.Where(s => InstallHeading().IsMatch(s.Heading)).Select(s => s.Text.ToString().Trim()).FirstOrDefault();
        string? changelog = sections.Where(s => ChangelogHeading().IsMatch(s.Heading)).Select(s => s.Text.ToString().Trim()).FirstOrDefault();

        // No changelog heading: use every section but the install one, with its heading.
        changelog ??= string.Join("\n", sections.Where(s => !InstallHeading().IsMatch(s.Heading)).Select(s => $"### {s.Heading}\n{s.Text}"));

        return new(preamble.ToString().Trim(), install is { Length: > 0 } ? install : null, changelog.Trim());
    }

    /// <summary>
    /// The changelog as Discord lines: "### Area" headings become bold lines, bullets wrapped over several lines are joined,
    /// and the "Dev notes" section is left out.
    /// </summary>
    private static List<string> ChangelogLines(string changelog)
    {
        List<string> lines = [];
        bool devNotes = false;
        foreach (string raw in changelog.Split('\n'))
        {
            string line = raw.TrimEnd();
            if (line.Trim().Length == 0) continue;

            if (line.StartsWith('#'))
            {
                string heading = line.TrimStart('#').Trim().TrimEnd(':');
                devNotes = DevNotesHeading().IsMatch(heading);
                // A blank line before each area after the first.
                if (!devNotes && heading.Length > 0) lines.Add(lines.Count > 0 ? $"\n**{heading}**" : $"**{heading}**");
                continue;
            }

            if (devNotes) continue;

            bool wrapped = char.IsWhiteSpace(line[0]) && !IsBullet(line) && lines.Count > 0 && IsBullet(lines[^1]);
            if (wrapped) lines[^1] += " " + line.Trim();
            else lines.Add(line);
        }

        return lines;
    }

    /// <summary>
    /// The whole changelog if it fits; otherwise the longest bullets cut to their first sentence until it fits; otherwise as
    /// many of the cut bullets as fit. Whenever anything is cut or left out, a note at the end says so and points to the release.
    /// </summary>
    private static string FitChangelog(List<string> lines, int budget)
    {
        if (lines.Count == 0) return "See the release on GitHub.";

        string full = string.Join("\n", lines);
        if (full.Length <= budget) return full;

        string shortenedNote = TrimmedNote(leftOut: 0);
        List<string> shortened = [.. lines];
        int total = full.Length + shortenedNote.Length;
        foreach (int i in Enumerable.Range(0, lines.Count).Where(i => IsBullet(lines[i])).OrderByDescending(i => lines[i].Length))
        {
            shortened[i] = Shorten(lines[i]);
            total -= lines[i].Length - shortened[i].Length;
            if (total <= budget) return string.Join("\n", shortened) + shortenedNote;
        }

        List<string> kept = [];
        int length = 0;
        for (int i = 0; i < shortened.Count; i++)
        {
            string note = TrimmedNote(shortened.Skip(i).Count(IsBullet));
            if (length + shortened[i].Length + 1 + note.Length > budget)
            {
                // Don't end on an area heading with nothing under it.
                while (kept.Count > 0 && !IsBullet(kept[^1])) kept.RemoveAt(kept.Count - 1);
                return string.Join("\n", kept) + TrimmedNote(shortened.Count(IsBullet) - kept.Count(IsBullet));
            }

            kept.Add(shortened[i]);
            length += shortened[i].Length + 1;
        }

        return string.Join("\n", kept) + shortenedNote;
    }

    /// <summary>The small line at the end of a changelog that was shortened, and maybe had changes left out, to fit.</summary>
    private static string TrimmedNote(int leftOut) =>
        "\n\n-# Changelog shortened to fit Discord's message limit" +
        (leftOut > 0 ? $"; {leftOut} more {(leftOut == 1 ? "change" : "changes")} left out" : "") +
        ". See the release link below for the full changelog.";

    private static bool IsBullet(string line)
    {
        string trimmed = line.TrimStart();
        return trimmed.StartsWith("- ", StringComparison.Ordinal) || trimmed.StartsWith("* ", StringComparison.Ordinal);
    }

    /// <summary>A bullet cut to its first sentence, with any bold or code it cut open closed again.</summary>
    private static string Shorten(string bullet)
    {
        int end = bullet.IndexOf(". ", StringComparison.Ordinal);
        string text = end > 0 ? bullet[..(end + 1)] : bullet;
        if (text.Length > MaxShortBulletLength)
        {
            int space = text.LastIndexOf(' ', MaxShortBulletLength - 1);
            text = text[..(space > MaxShortBulletLength / 2 ? space : MaxShortBulletLength - 1)].TrimEnd(' ', ',', ';', ':') + "…";
        }

        if (CountOf(text, "**") % 2 == 1) text += "**";
        if (CountOf(text.Replace("**", ""), "`") % 2 == 1) text += "`";
        return text;
    }

    private static int CountOf(string text, string part) => (text.Length - text.Replace(part, "").Length) / part.Length;

    /// <summary>Text cut at the last line break that fits, so long sections can't crowd out the rest.</summary>
    private static string Cap(string text, int max)
    {
        if (text.Length <= max) return text;
        int cut = text.LastIndexOf('\n', max - 2);
        return text[..(cut > 0 ? cut : max - 2)].TrimEnd() + "\n…";
    }

    /// <summary>Wrap links in &lt;&gt; so Discord doesn't add a preview of every page.</summary>
    private static string SuppressEmbeds(string text) =>
        BareUrl().Replace(MarkdownLink().Replace(text, "](<$1>)"), "<$0>");

    [GeneratedRegex(@"\]\((https?://[^)\s>]+)\)")]
    private static partial Regex MarkdownLink();

    [GeneratedRegex(@"(?<![<(\[\w])https?://[^\s<>()\]]+")]
    private static partial Regex BareUrl();

    [GeneratedRegex("install|updat", RegexOptions.IgnoreCase)]
    private static partial Regex InstallHeading();

    [GeneratedRegex(@"change\s*log|what'?s new|changes", RegexOptions.IgnoreCase)]
    private static partial Regex ChangelogHeading();

    [GeneratedRegex(@"\bdev\b|developer", RegexOptions.IgnoreCase)]
    private static partial Regex DevNotesHeading();
}
