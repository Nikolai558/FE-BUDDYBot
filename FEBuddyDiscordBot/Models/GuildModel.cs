using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace FEBuddyDiscordBot.Models;
public class GuildModel
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; }
    public ulong GuildId { get; set; }
    public string GuildName { get; set; }
    public GuildSettings Settings { get; set; }
    public DateTime RecordCreated { get; set; } = DateTime.UtcNow;
}

public class GuildSettings
{
    public string Prefix { get; set; }
    public string VerifiedRoleName { get; set; }
}