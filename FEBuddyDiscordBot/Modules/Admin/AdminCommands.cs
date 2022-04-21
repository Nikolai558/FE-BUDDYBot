namespace FEBuddyDiscordBot.Modules.Admin;

/// <summary>
/// Discord Admin Only Commands
/// </summary>
[Name("Admin Commands")]
[Summary("These commands are to assist the Administrators of the server.")]
[RequireUserPermission(Discord.GuildPermission.Administrator)]
public class AdminCommands: ModuleBase
{
    // Dependency Injection services Required
    private readonly IServiceProvider _services;
    private readonly IConfiguration _config;
    private readonly DiscordSocketClient _discord;
    private readonly ILogger _logger;

    /// <summary>
    /// Initialize the Admin Commands Module (This might be unnecessary)
    /// </summary>
    /// <param name="services">Dependency Injection Service Provider</param>
    public AdminCommands(IServiceProvider services)
    {
        _services = services;
        _config = _services.GetRequiredService<IConfiguration>();
        _discord = _services.GetRequiredService<DiscordSocketClient>();
        _logger = _services.GetRequiredService<ILogger<AdminCommands>>();

        _logger.LogDebug("Module: Loaded AdminCommands");
    }

    // Discord Server Admin only commands go here.
}
