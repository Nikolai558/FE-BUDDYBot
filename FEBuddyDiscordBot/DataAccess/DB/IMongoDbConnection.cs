using FEBuddyDiscordBot.Models;
using MongoDB.Driver;

namespace FEBuddyDiscordBot.DataAccess.DB;

public interface IMongoDbConnection
{
    MongoClient Client { get; }
    string DbName { get; }
    IMongoCollection<GuildModel> GuildCollection { get; }
    string GuildCollectionName { get; }
}