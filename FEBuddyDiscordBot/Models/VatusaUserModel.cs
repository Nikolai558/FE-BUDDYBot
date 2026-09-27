using System.Text.Json.Serialization;

namespace FEBuddyDiscordBot.Models;

/// <summary>
/// Response wrapper from GET https://api.vatusa.net/v2/user/{discordId}?d
/// Only the fields the bot uses are mapped.
/// </summary>
public sealed class VatusaUserResponse
{
    [JsonPropertyName("data")]
    public VatusaUser? Data { get; set; }
}

public sealed class VatusaUser
{
    [JsonPropertyName("cid")]
    public long? Cid { get; set; }

    [JsonPropertyName("fname")]
    public string? FirstName { get; set; }

    [JsonPropertyName("lname")]
    public string? LastName { get; set; }

    [JsonPropertyName("facility")]
    public string? Facility { get; set; }

    [JsonPropertyName("rating_short")]
    public string? RatingShort { get; set; }

    /// <summary>When true the member asked VATUSA to hide their real name, so the bot uses their CID instead.</summary>
    [JsonPropertyName("flag_nameprivacy")]
    public bool? NamePrivacy { get; set; }

    [JsonPropertyName("roles")]
    public VatusaStaffRole[]? Roles { get; set; }
}

public sealed class VatusaStaffRole
{
    [JsonPropertyName("facility")]
    public string? Facility { get; set; }

    [JsonPropertyName("role")]
    public string? Role { get; set; }
}
