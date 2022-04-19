using FEBuddyDiscordBot.Models;

namespace FEBuddyDiscordBot.DataAccess.DB;

public interface IMongoGuildData
{
    Task CreateGuild(GuildModel guild);
    Task<List<GuildModel>> GetAllGuildsAsync();
    Task<GuildModel> GetGuildAsync(ulong id);
    Task UpdateGuild(GuildModel guild);
}