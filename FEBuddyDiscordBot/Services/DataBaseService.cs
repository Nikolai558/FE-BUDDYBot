using FEBuddyDiscordBot.DataAccess.DB;
using FEBuddyDiscordBot.Models;
using System;
using System.Collections.Generic;
using System.Threading;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FEBuddyDiscordBot.Services;
public class DataBaseService
{
    private readonly IServiceProvider _services;
    private readonly IConfiguration _config;
    private readonly ILogger _logger;
    private IMongoGuildData _guildData;

    public DataBaseService(IServiceProvider services)
    {
        _services = services;
        _config = _services.GetRequiredService<IConfiguration>();
        _logger = _services.GetRequiredService<ILogger<StartupService>>();
        _guildData = _services.GetRequiredService<IMongoGuildData>();

    }

    public async Task CheckGuilds(IReadOnlyCollection<SocketGuild> guildList)
    {
        foreach (var guild in guildList)
        {
            GuildModel foundGuildInDb = await _guildData.GetGuildAsync(guild.Id);
            if (foundGuildInDb != null)
            {
                _logger.LogInformation($"DataBase: Found Guild, {guild.Name} ({guild.Id}), in DB");
            }
            else
            {
                _logger.LogInformation($"DataBase: Did NOT find Guild, {guild.Name} ({guild.Id}), in DB");

                GuildModel newGuild = new GuildModel
                {
                    GuildId = guild.Id,
                    GuildName = guild.Name,
                    Settings = new GuildSettings 
                    { 
                        Prefix = _config.GetSection("DefaultBotSettings").GetSection("Prefix").Value, 
                        VerifiedRoleName = _config.GetSection("DefaultBotSettings").GetSection("VerifiedRole").Value
                    }
                };
                await _guildData.CreateGuild(newGuild);

                _logger.LogInformation($"DataBase: Added Guild, {guild.Name} ({guild.Id}), to DB with default options");
            }
        }
    }
}
