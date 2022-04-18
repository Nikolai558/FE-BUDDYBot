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
            GuildModel foundGuildInDb = await _guildData.GetGuildAsync(guild.Id.ToString());
            if (foundGuildInDb != null)
            {
                _logger.LogInformation("TEST: Found Guild in DB");
            }
            else
            {
                _logger.LogInformation("TEST: Did NOT find Guild in DB");

                GuildModel newGuild = new GuildModel
                {
                    Name = guild.Name,
                    GuildId = guild.Id.ToString()
                };
                await _guildData.CreateGuild(newGuild);

                _logger.LogInformation($"TEST: Added {guild.Name} ({guild.Id}) to DB");
            }
        }
    }
}
