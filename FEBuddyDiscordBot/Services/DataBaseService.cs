using FEBuddyDiscordBot.DataAccess.DB;
using FEBuddyDiscordBot.Models;

namespace FEBuddyDiscordBot.Services;

/// <summary>
/// Database Helper Services
/// </summary>
public class DataBaseService
{
    // Dependency Injection Services Required
    private readonly IServiceProvider _services;
    private readonly IConfiguration _config;
    private readonly ILogger _logger;
    private readonly IMongoGuildData _guildData;

    /// <summary>
    /// Database Service Constructor
    /// </summary>
    /// <param name="services">Dependency Injection Service Provider</param>
    public DataBaseService(IServiceProvider services)
    {
        _services = services;
        _config = _services.GetRequiredService<IConfiguration>();
        _logger = _services.GetRequiredService<ILogger<StartupService>>();
        _guildData = _services.GetRequiredService<IMongoGuildData>();

        _logger.LogDebug("Loaded: DataBaseService");
    }

    public async Task CheckGuild(SocketGuild guild)
    {
        GuildModel foundGuildInDb = await _guildData.GetGuildAsync(guild.Id);
        if (foundGuildInDb != null)
        {
            // Found guild in database, Nothing further required
            _logger.LogInformation($"DataBase: Found Guild, {guild.Name} ({guild.Id}), in DB");
        }
        else
        {
            // Did not find guild in database, we need to initialize it with default settings inside the database.
            _logger.LogInformation($"DataBase: Did NOT find Guild, {guild.Name} ({guild.Id}), in DB");

            GuildSettings defaultSettings = new()
            {
                Prefix = _config.GetSection("DefaultBotSettings").GetSection("Prefix").Value,

                AutoAssignRoles_OnJoin = bool.Parse(_config.GetSection("DefaultBotSettings").GetSection("AutoAssignRoles_OnJoin").Value),
                AutoAssignRoles_OnVoiceChannelJoin = bool.Parse(_config.GetSection("DefaultBotSettings").GetSection("AutoAssignRoles_OnVoiceChannelJoin").Value),
                AssignPrivateMeetingRole_OnVoiceChannelJoin = bool.Parse(_config.GetSection("DefaultBotSettings").GetSection("AssignPrivateMeetingRole_OnVoiceChannelJoin").Value),
                AutoChangeNicknames = bool.Parse(_config.GetSection("DefaultBotSettings").GetSection("AutoChangeNicknames").Value),
                AssignArtccStaffRole = bool.Parse(_config.GetSection("DefaultBotSettings").GetSection("AssignArtccStaffRole").Value),

                PrivateMeetingVoiceChannelName = _config.GetSection("DefaultBotSettings").GetSection("PrivateMeetingVoiceChannelName").Value,
                PrivateMeetingRole = _config.GetSection("DefaultBotSettings").GetSection("PrivateMeetingRole").Value,

                VerifiedRoleName = _config.GetSection("DefaultBotSettings").GetSection("VerifiedRole").Value,
                ArtccStaffRoleName = _config.GetSection("DefaultBotSettings").GetSection("ArtccStaffRoleName").Value,
                RolesTextChannelName = _config.GetSection("DefaultBotSettings").GetSection("RolesTextChannelName").Value,
            };

            GuildModel newGuild = new()
            {
                GuildId = guild.Id,
                GuildName = guild.Name,
                Settings = defaultSettings
            };
            await _guildData.CreateGuild(newGuild);

            _logger.LogInformation($"DataBase: Added Guild, {guild.Name} ({guild.Id}), to DB with default options");
        }
    }

    /// <summary>
    /// Check the database for a list of guilds and verify they are in the database
    /// </summary>
    /// <param name="guildList">Socket Guild List/Collection</param>
    /// <returns>None</returns>
    public async Task CheckGuilds(IReadOnlyCollection<SocketGuild> guildList)
    {
        foreach (var guild in guildList)
        {
            GuildModel foundGuildInDb = await _guildData.GetGuildAsync(guild.Id);
            if (foundGuildInDb != null)
            {
                // Found guild in database, Nothing further required
                _logger.LogInformation($"DataBase: Found Guild, {guild.Name} ({guild.Id}), in DB");
            }
            else
            {
                // Did not find guild in database, we need to initialize it with default settings inside the database.
                _logger.LogInformation($"DataBase: Did NOT find Guild, {guild.Name} ({guild.Id}), in DB");

                GuildSettings defaultSettings = new()
                {
                    Prefix = _config.GetSection("DefaultBotSettings").GetSection("Prefix").Value,

                    AutoAssignRoles_OnJoin = bool.Parse(_config.GetSection("DefaultBotSettings").GetSection("AutoAssignRoles_OnJoin").Value),
                    AutoAssignRoles_OnVoiceChannelJoin = bool.Parse(_config.GetSection("DefaultBotSettings").GetSection("AutoAssignRoles_OnVoiceChannelJoin").Value),
                    AssignPrivateMeetingRole_OnVoiceChannelJoin = bool.Parse(_config.GetSection("DefaultBotSettings").GetSection("AssignPrivateMeetingRole_OnVoiceChannelJoin").Value),
                    AutoChangeNicknames = bool.Parse(_config.GetSection("DefaultBotSettings").GetSection("AutoChangeNicknames").Value),
                    AssignArtccStaffRole = bool.Parse(_config.GetSection("DefaultBotSettings").GetSection("AssignArtccStaffRole").Value),

                    PrivateMeetingVoiceChannelName = _config.GetSection("DefaultBotSettings").GetSection("PrivateMeetingVoiceChannelName").Value,
                    PrivateMeetingRole = _config.GetSection("DefaultBotSettings").GetSection("PrivateMeetingRole").Value,

                    VerifiedRoleName = _config.GetSection("DefaultBotSettings").GetSection("VerifiedRole").Value,
                    ArtccStaffRoleName = _config.GetSection("DefaultBotSettings").GetSection("ArtccStaffRoleName").Value,
                    RolesTextChannelName = _config.GetSection("DefaultBotSettings").GetSection("RolesTextChannelName").Value,
                };

                GuildModel newGuild = new()
                {
                    GuildId = guild.Id,
                    GuildName = guild.Name,
                    Settings = defaultSettings
                };
                await _guildData.CreateGuild(newGuild);

                _logger.LogInformation($"DataBase: Added Guild, {guild.Name} ({guild.Id}), to DB with default options");
            }
        }
    }
}
