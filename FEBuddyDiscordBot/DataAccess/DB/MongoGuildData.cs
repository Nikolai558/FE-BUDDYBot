using FEBuddyDiscordBot.Models;
using MongoDB.Driver;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FEBuddyDiscordBot.DataAccess.DB;

/// <summary>
/// Mongo Database Access
/// </summary>
public class MongoGuildData : IMongoGuildData
{
    private readonly IMongoCollection<GuildModel> _guilds;

    /// <summary>
    /// MongoDB Access
    /// </summary>
    /// <param name="db">Mongo Database Connection</param>
    public MongoGuildData(IMongoDbConnection db)
    {
        _guilds = db.GuildCollection;
    }
    
    /// <summary>
    /// Get all guilds from the database
    /// </summary>
    /// <returns>List of Guild Models</returns>
    public async Task<List<GuildModel>> GetAllGuildsAsync()
    {
        var results = await _guilds.FindAsync(_ => true);
        return results.ToList();
    }

    /// <summary>
    /// Get a guild from the database by the guild ID
    /// </summary>
    /// <param name="id">Guild (discord server) ID</param>
    /// <returns>Guild Model</returns>
    public async Task<GuildModel> GetGuildAsync(ulong id)
    {
        var results = await _guilds.FindAsync(guild => guild.GuildId == id);
        return results.FirstOrDefault();
    }

    /// <summary>
    /// Add a guild model to the database
    /// </summary>
    /// <param name="guild">Guild Model to be added.</param>
    /// <returns>None</returns>
    public Task CreateGuild(GuildModel guild)
    {
        return _guilds.InsertOneAsync(guild);
    }

    /// <summary>
    /// Update a guild in the database
    /// </summary>
    /// <param name="guild">Guild Model to be updated</param>
    /// <returns>None</returns>
    public Task UpdateGuild(GuildModel guild)
    {
        var filter = Builders<GuildModel>.Filter.Eq("Id", guild.GuildId);
        return _guilds.ReplaceOneAsync(filter, guild, new ReplaceOptions { IsUpsert = true });
    }
}
