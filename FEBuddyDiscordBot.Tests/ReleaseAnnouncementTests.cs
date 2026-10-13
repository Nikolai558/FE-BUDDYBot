using FEBuddyDiscordBot.Announcements;

namespace FEBuddyDiscordBot.Tests;

public class ReleaseAnnouncementTests
{
    // FE-BUDDY 3.0.0-beta.10's release notes.
    private const string BetaNotes = """
        > **Pre-release (Beta):** for testing, and it may have bugs. FE-BUDDY's updater only offers it if your update channel in Settings includes Beta releases.

        ## Instructions to install:
        - **Already have FE-BUDDY 2.9 or newer:** click **Update available!** at the top of FE-BUDDY's window (in 2.9.x, accept the update prompt). It downloads and runs the new installer for you.
        - **New install, or auto-update isn't working:** [Download FE-BUDDY-Setup.msi](https://github.com/Nikolai558/FE-BUDDY/releases/download/3.0.0-beta.10/FE-BUDDY-Setup.msi), run it, and follow the prompts.


        ## Change log:
        ### AIRAC Service
        - Bug #361 - FAA Chart Recall: an approach's runway no longer keeps a leading zero, so a runway the
          FAA prints as both `RWY 06` and `RWY 6` gets one command, `.aaaI6c`, instead of `.aaaI06c` and
          `.aaaI6c`. `ILS Z OR LOC RWY 04L` at DTW is now `.dtwIZ4Lc`.

        ### Info
        - Bug #361 - **What's New**, the **Alias Command Guide** (and its Markdown and HTML exports) and the
          **Alias Command Practice** show runways without a leading zero, and the practice now hints to
          leave the zero out.

        ### Dev notes
        - Secret internals nobody outside the project needs.

        ---
        [Every change since 3.0.0-beta.9](https://github.com/Nikolai558/FE-BUDDY/compare/3.0.0-beta.9...3.0.0-beta.10) ·
        `FE-BUDDY-Setup.msi` SHA-256: `34980f51d618e7b1a170182d1622b1c48249b02e77a0063bd696f35fe02f27cf`
        """;

    private const string Url = "https://github.com/Nikolai558/FE-BUDDY/releases/tag/3.0.0-beta.10";

    [Theory]
    [InlineData("3.0.0-beta.10", true, ReleaseLevel.Beta)]
    [InlineData("v3.0.0-alpha.5", true, ReleaseLevel.Alpha)]
    [InlineData("3.0.0-rc.1", true, ReleaseLevel.ReleaseCandidate)]
    [InlineData("3.0.0-RC2", true, ReleaseLevel.ReleaseCandidate)]
    [InlineData("2.9.3", false, ReleaseLevel.Stable)]
    [InlineData("v2.9.3", false, ReleaseLevel.Stable)]
    [InlineData("3.0.0-preview.1", true, ReleaseLevel.Alpha)]
    [InlineData("3.0.0", true, ReleaseLevel.Alpha)] // marked pre-release on GitHub with no suffix
    public void Release_level_comes_from_the_tag(string tag, bool prerelease, ReleaseLevel expected)
    {
        Assert.Equal(expected, ReleaseLevels.FromTag(tag, prerelease));
    }

    [Fact]
    public void Levels_are_ordered_least_to_most_finished()
    {
        Assert.True(ReleaseLevel.Alpha < ReleaseLevel.Beta);
        Assert.True(ReleaseLevel.Beta < ReleaseLevel.ReleaseCandidate);
        Assert.True(ReleaseLevel.ReleaseCandidate < ReleaseLevel.Stable);
    }

    [Fact]
    public void Announcement_has_the_title_note_changelog_install_steps_and_link()
    {
        string text = ReleaseAnnouncement.Build("3.0.0-beta.10", ReleaseLevel.Beta, Url, BetaNotes, "<:FEB_cheer:123>");

        Assert.StartsWith("## FE-BUDDY 3.0.0-beta.10 is released! <:FEB_cheer:123>\n> **Pre-release (Beta):**", text);
        Assert.Contains("### Changelog\n**AIRAC Service**\n- Bug #361 - FAA Chart Recall: an approach's runway no longer keeps a leading zero, so a runway the FAA prints", text);
        Assert.Contains("**Info**", text);
        Assert.Contains("### How to update\n- **Already have FE-BUDDY 2.9 or newer:**", text);
        Assert.Contains("[Download FE-BUDDY-Setup.msi](<https://github.com/Nikolai558/FE-BUDDY/releases/download/3.0.0-beta.10/FE-BUDDY-Setup.msi>)", text);
        Assert.EndsWith($"[Pre-release 3.0.0-beta.10 on GitHub](<{Url}>)", text);

        Assert.DoesNotContain("Dev notes", text);
        Assert.DoesNotContain("Secret internals", text);
        Assert.DoesNotContain("SHA-256", text);
        Assert.DoesNotContain("shortened", text); // it all fit
        Assert.True(text.Length <= ReleaseAnnouncement.MaxLength);
    }

    [Fact]
    public void Stable_release_without_notes_gets_default_steps_and_no_pre_release_note()
    {
        string text = ReleaseAnnouncement.Build("3.0.0", ReleaseLevel.Stable, Url, null, null);

        Assert.StartsWith("## FE-BUDDY 3.0.0 is released!\n\n### Changelog\nSee the release on GitHub.", text);
        Assert.Contains("click **Update available!**", text);
        Assert.DoesNotContain("pre-release", text, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith($"[Release 3.0.0 on GitHub](<{Url}>)", text);
    }

    [Fact]
    public void Pre_release_without_a_note_gets_one()
    {
        string text = ReleaseAnnouncement.Build("3.0.0-rc.1", ReleaseLevel.ReleaseCandidate, Url, "- Fixed a thing", null);

        Assert.Contains("> **Release candidate pre-release:** for testing, and it may have bugs.", text);
        Assert.Contains("### Changelog\n- Fixed a thing\n", text);
    }

    [Fact]
    public void Only_the_longest_bullets_are_cut_to_their_first_sentence()
    {
        string bullets = "- The **big** change. " + string.Concat(Enumerable.Repeat("Then a lot more detail follows here. ", 40)) + "\n" +
                         string.Join("\n", Enumerable.Range(1, 5).Select(i => $"- Small change {i}. It has a second sentence."));
        string text = ReleaseAnnouncement.Build("3.0.0", ReleaseLevel.Stable, Url, $"## Change log:\n{bullets}", null);

        Assert.True(text.Length <= ReleaseAnnouncement.MaxLength);
        Assert.Contains("- The **big** change.\n", text);
        Assert.DoesNotContain("Then a lot more", text);
        for (int i = 1; i <= 5; i++) Assert.Contains($"- Small change {i}. It has a second sentence.", text);
        Assert.Contains("- Small change 5. It has a second sentence.\n\n-# Changelog shortened to fit Discord's message limit. " +
                        "See the release link below for the full changelog.\n\n### How to update", text);
    }

    [Fact]
    public void A_cut_bullet_ends_on_a_whole_word_and_closes_its_bold()
    {
        string bullet = "- **" + string.Join(" ", Enumerable.Repeat("word", 60)) + "** done";
        string text = ReleaseAnnouncement.Build("3.0.0", ReleaseLevel.Stable, Url, $"## Change log:\n{string.Join("\n", Enumerable.Repeat(bullet, 8))}", null);

        Assert.Contains("word word…**\n", text);
    }

    [Fact]
    public void A_huge_changelog_keeps_what_fits_and_counts_the_rest()
    {
        string sections = string.Join("\n", Enumerable.Range(1, 12).Select(s =>
            $"### Area {s}\n" + string.Join("\n", Enumerable.Range(1, 6).Select(i => $"- Area {s} change {i} with a reasonably long description of what changed"))));
        string text = ReleaseAnnouncement.Build("3.0.0", ReleaseLevel.Stable, Url, $"## Change log:\n{sections}", null);

        Assert.True(text.Length <= ReleaseAnnouncement.MaxLength);
        Assert.Contains("- Area 1 change 1", text);
        Assert.DoesNotContain("Area 12 change 6", text);
        Assert.Matches(@"- Area \d+ change \d[^\n]*\n\n-# Changelog shortened to fit Discord's message limit; \d+ more changes left out\. " +
                       @"See the release link below for the full changelog\.\n\n### How to update", text);

        // The note says exactly how many changes are missing.
        int shown = text.Split('\n').Count(l => l.StartsWith("- Area", StringComparison.Ordinal));
        Assert.Contains($"; {72 - shown} more changes left out", text);
    }

    [Fact]
    public void Notes_without_headings_are_the_changelog()
    {
        string text = ReleaseAnnouncement.Build("2.9.4", ReleaseLevel.Stable, Url, "Fixed the menu links. See https://example.com/docs for more.", null);

        Assert.Contains("### Changelog\nFixed the menu links. See <https://example.com/docs> for more.\n", text);
    }
}
