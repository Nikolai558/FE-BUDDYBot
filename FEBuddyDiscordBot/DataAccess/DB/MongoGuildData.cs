using FEBuddyDiscordBot.Models;
using MongoDB.Driver;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FEBuddyDiscordBot.DataAccess.DB;
public class MongoGuildData : IMongoGuildData
{
    private readonly IMongoCollection<GuildModel> _guilds;

    public MongoGuildData(IMongoDbConnection db)
    {
        _guilds = db.GuildCollection;
    }
    

    public async Task<List<GuildModel>> GetAllGuildsAsync()
    {
        var results = await _guilds.FindAsync(_ => true);
        return results.ToList();
    }

    public async Task<GuildModel> GetGuildAsync(ulong id)
    {
        var results = await _guilds.FindAsync(guild => guild.GuildId == id);
        return results.First();
    }

    public Task CreateGuild(GuildModel guild)
    {
        return _guilds.InsertOneAsync(guild);
    }

    public Task UpdateGuild(GuildModel guild)
    {
        var filter = Builders<GuildModel>.Filter.Eq("Id", guild.GuildId);
        return _guilds.ReplaceOneAsync(filter, guild, new ReplaceOptions { IsUpsert = true });
    }
}
