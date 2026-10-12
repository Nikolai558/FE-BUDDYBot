namespace FEBuddyDiscordBot.Airac;

/// <summary>
/// The message posted when the next cycle's d-TPP Metafile is out, with a button to stop the pings.
/// </summary>
public static class MetafilePing
{
    public const string StopButtonId = "airac-pings:off";

    public static string Text(AiracCycle cycle, ulong roleId) =>
        $"<@&{roleId}>\n" +
        $"## 📢 AIRAC {cycle.Id} MetaFile is available\n" +
        $"The d-TPP MetaFile for the next cycle is out. You can now run FE-Buddy for AIRAC **{cycle.Id}** and get your full export.\n" +
        $"-# Cycle {cycle.Id} takes effect {cycle.EffectiveDate:MMMM d, yyyy}.";

    public static MessageComponent Buttons() => new ComponentBuilder()
        .WithButton("Stop these pings", StopButtonId, ButtonStyle.Secondary, new Emoji("🔕"))
        .Build();
}
