using FEBuddyDiscordBot.Models;

namespace FEBuddyDiscordBot.Announcements;

/// <summary>
/// The preview posted in the review channel: the announcement's text as the message, an embed saying what each button does,
/// and Post, Edit and Cancel buttons. Every custom ID starts with "release:" and ends with the release's tag.
/// </summary>
public static class ReleasePreview
{
    public const string Prefix = "release:";

    public static MessageComponent Buttons(string tag) => new ComponentBuilder()
        .WithButton("Post", $"{Prefix}post:{tag}", ButtonStyle.Success)
        .WithButton("Edit", $"{Prefix}edit:{tag}", ButtonStyle.Primary)
        .WithButton("Cancel", $"{Prefix}cancel:{tag}", ButtonStyle.Danger)
        .Build();

    /// <summary>
    /// The button under each posted announcement that gives or takes away the ping role. It doesn't start with
    /// <see cref="Prefix"/>: it's handled by <see cref="Modules.SlashCommands.ReleasePingCommands"/>, not the preview handler.
    /// </summary>
    public const string PingToggleButtonId = "release-pings:toggle";

    public static MessageComponent PingToggleButton() => new ComponentBuilder()
        .WithButton("Opt in/out of release notifications", PingToggleButtonId, ButtonStyle.Secondary)
        .Build();

    public static Embed PendingEmbed(string version, ReleaseLevel level, string releaseUrl, GuildSettings settings)
    {
        string where = settings.ReleaseChannelId is ulong channelId ? $"<#{channelId}>" : "the announcement channel (not set yet: `/admin releases channel:`)";
        string ping = settings.ReleasePingRoleId is ulong roleId ? $" and pings <@&{roleId}>" : " without a ping";

        return new EmbedBuilder()
            .WithTitle($"Preview: FE-BUDDY {version}")
            .WithUrl(releaseUrl)
            .WithDescription(
                $"A new {ReleaseLevels.Name(level).ToLowerInvariant()} release is on GitHub. The message above is the announcement; it hasn't been posted.\n" +
                $"- **Post** sends it to {where}{ping}, then replaces this preview with a note of who posted it.\n" +
                "- **Edit** changes its text first.\n" +
                "- **Cancel** drops it and leaves a note that the release needs announcing by hand.")
            .WithColor(Color.Orange)
            .Build();
    }
}
