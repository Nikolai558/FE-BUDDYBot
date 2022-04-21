using MongoDB.Driver;
using FEBuddyDiscordBot.Models;

namespace FEBuddyDiscordBot.DataAccess.DB;
public class MongoDbConnection : IMongoDbConnection
{
    private readonly string _connectionId = "MongoDB";
    private readonly IConfiguration _config;
    private readonly IMongoDatabase _db;

    public string DbName { get; private set; }
    public string GuildCollectionName { get; private set; } = "discord-guilds";

    public MongoClient Client { get; private set; }
    public IMongoCollection<GuildModel> GuildCollection { get; private set; }

    public MongoDbConnection(IConfiguration config)
    {
        _config = config;
        Client = new MongoClient(_config.GetConnectionString(_connectionId));
        DbName = _config["DatabaseName"];
        _db = Client.GetDatabase(DbName);

        GuildCollection = _db.GetCollection<GuildModel>(GuildCollectionName);
    }
}
