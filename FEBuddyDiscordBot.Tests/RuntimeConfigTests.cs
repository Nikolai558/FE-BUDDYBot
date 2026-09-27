using System.Text.Json;

namespace FEBuddyDiscordBot.Tests;

public class RuntimeConfigTests
{
    // Discord.Net builds CultureInfo objects from each server's locale. In globalization-invariant mode that throws,
    // and the bot silently fails to load the server's roles and channels (this happened on the first deploy).
    [Fact]
    public void Bot_does_not_run_in_globalization_invariant_mode()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "FEBuddyDiscordBot.runtimeconfig.json");
        using JsonDocument config = JsonDocument.Parse(File.ReadAllText(path));

        bool invariant = config.RootElement.GetProperty("runtimeOptions").TryGetProperty("configProperties", out JsonElement properties)
            && properties.TryGetProperty("System.Globalization.Invariant", out JsonElement value)
            && value.GetBoolean();

        Assert.False(invariant, "InvariantGlobalization must stay off for FEBuddyDiscordBot.");
    }
}
