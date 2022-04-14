using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace FEBuddyDiscordBot.Models;
public class GuildModel
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; }
    public string GuildId { get; set; }
    public string Name { get; set; }
}
