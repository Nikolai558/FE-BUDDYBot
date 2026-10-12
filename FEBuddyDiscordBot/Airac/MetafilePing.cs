namespace FEBuddyDiscordBot.Airac;

/// <summary>
/// The message posted when the next cycle's d-TPP Metafile is out, with a button to stop the pings.
/// </summary>
public static class MetafilePing
{
    public const string StopButtonId = "airac-pings:off";

    public static string Text(string cycleId, ulong roleId) =>
        $"<@&{roleId}> AIRAC `{cycleId}` MetaFile is now available!\n\n" +
        "You may now run FE-Buddy for the next cycle and get your full AIRAC export.\n\n" +
        "--\n" +
        "> To stop getting these pings:\n" +
        "> Press **Stop these pings** below, or run `/airac-pings turn:Off`.";

    public static MessageComponent Buttons() => new ComponentBuilder()
        .WithButton("Stop these pings", StopButtonId, ButtonStyle.Secondary, new Emoji("🔕"))
        .Build();
}
