using Discord.Interactions;
using FEBuddyDiscordBot.DataAccess.DB;

namespace FEBuddyDiscordBot.Modules.SlashCommands;

/// <summary>
/// Admin Discord Slash Commands and Interactions
/// </summary>
public class AdminSlashCommands: InteractionModuleBase<SocketInteractionContext>
{
    // Dependency Injection Services Required
    private readonly IServiceProvider _services;
    private readonly IConfiguration _config;
    private readonly DiscordSocketClient _discord;
    private readonly ILogger _logger;
    private readonly IMongoGuildData _guildData;

    /// <summary>
    /// Initialize the Admin Slash Commands Module (This might be unnecessary)
    /// </summary>
    /// <param name="services">Dependency Injection Service Provider</param>
    public AdminSlashCommands(IServiceProvider services)
    {
        _services = services;
        _config = _services.GetRequiredService<IConfiguration>();
        _discord = _services.GetRequiredService<DiscordSocketClient>();
        _logger = _services.GetRequiredService<ILogger<AdminSlashCommands>>();
        _guildData = _services.GetRequiredService<IMongoGuildData>();

        _logger.LogDebug("Module: Loaded AdminSlashCommands");
    }
}
