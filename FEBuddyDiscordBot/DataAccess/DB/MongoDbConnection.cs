using FEBuddyDiscordBot.Models;
using MongoDB.Driver;

namespace FEBuddyDiscordBot.DataAccess.DB;

/// <summary>
/// Mongo Database Connection
/// </summary>
public class MongoDbConnection : IMongoDbConnection
{
    private readonly string _connectionId = "MongoDB";
    private readonly IConfiguration _config;
    private readonly IMongoDatabase _db;

    /// <summary>
    /// Database name
    /// </summary>
    public string DbName { get; private set; }

    /// <summary>
    /// MongoDB Collection name for all the guilds
    /// </summary>
    public string GuildCollectionName { get; private set; } = "discord-guilds";

    /// <summary>
    /// MongoDB Client
    /// </summary>
    public MongoClient Client { get; private set; }

    /// <summary>
    /// MongoDB Collection for the Guilds
    /// </summary>
    public IMongoCollection<GuildModel> GuildCollection { get; private set; }

    /// <summary>
    /// Open the MongoDB Connection 
    /// </summary>
    /// <param name="config">Dependency Injection Configuration</param>
    public MongoDbConnection(IConfiguration config)
    {
        _config = config;
        Client = new MongoClient(_config.GetConnectionString(_connectionId));
        DbName = _config["DatabaseName"];
        _db = Client.GetDatabase(DbName);

        GuildCollection = _db.GetCollection<GuildModel>(GuildCollectionName);
    }
}
