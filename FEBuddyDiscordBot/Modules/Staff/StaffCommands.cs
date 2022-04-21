namespace FEBuddyDiscordBot.Modules.Staff;

/// <summary>
/// Discord Staff Only Commands
/// </summary>
[Name("Staff Commands")]
[Summary("These commands are to assist the Staff of the server.")]
[RequireUserPermission(GuildPermission.ManageMessages | GuildPermission.ManageChannels)]
public class StaffCommands : ModuleBase
{
    // Dependency Injection services Required
    private readonly IServiceProvider _services;
    private readonly IConfiguration _config;
    private readonly DiscordSocketClient _discord;
    private readonly ILogger _logger;

    /// <summary>
    /// Initialize the Staff Commands Module (This might be unnecessary)
    /// </summary>
    /// <param name="services">Dependency Injection Service Provider</param>
    public StaffCommands(IServiceProvider services)
    {
        _services = services;
        _config = _services.GetRequiredService<IConfiguration>();
        _discord = _services.GetRequiredService<DiscordSocketClient>();
        _logger = _services.GetRequiredService<ILogger<StaffCommands>>();

        _logger.LogDebug("Module: Loaded StaffCommands");
    }

    // Discord Server Staff commands go here.
}
