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

    public bool AutoAssignRoles_OnJoin { get; set; }
    public bool AutoAssignRoles_OnVoiceChannelJoin { get; set; }
    public bool AssignPrivateMeetingRole_OnVoiceChannelJoin { get; set; }
    public bool AutoChangeNicknames { get; set; }
    public bool AssignArtccStaffRole { get; set; }

    public string PrivateMeetingVoiceChannelName { get; set; }
    public string PrivateMeetingRole { get; set; }

    public string VerifiedRoleName { get; set; }
    public string ArtccStaffRoleName { get; set; }
    public string RolesTextChannelName { get; set; }
}