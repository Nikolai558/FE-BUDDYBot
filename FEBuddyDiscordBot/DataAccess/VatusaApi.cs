using System.Net;
using System.Text.Json;
using static FEBuddyDiscordBot.Models.VatusaUserModel;

namespace FEBuddyDiscordBot.DataAccess;

/// <summary>
/// Access to the VATUSA API. Note some information returned will be null unless a VATUSA API TOKEN is used.
/// </summary>
public class VatusaApi
{
    /// <summary>
    /// Get the User information from the users Discord Id
    /// </summary>
    /// <param name="MemberId">Users Discord Id</param>
    /// <returns>VATUSA User Data Model</returns>
    public static async Task<VatusaUserData?> GetVatusaUserInfo(ulong MemberId)
    {
        string url = $"https://api.vatusa.net/v2/user/{MemberId}?d";

        using HttpClient httpClient = new();

        try
        {
            string json = await httpClient.GetStringAsync(url);
            VatusaUserData? userData = JsonSerializer.Deserialize<VatusaUserData>(json);
            return userData;
        }
        catch (WebException)
        {
            return null;
        }
    }

    /// <summary>
    /// Get the User information from the users VATUSA CID Number.
    /// </summary>
    /// <param name="cid">User's CID Number</param>
    /// <returns>VATUSA User Data Model</returns>
    public static async Task<VatusaUserData?> GetVatusaUserInfo(string cid)
    {
        string url = $"https://api.vatusa.net/v2/user/{cid}";

        using HttpClient httpClient = new();

        try
        {
            string json = await httpClient.GetStringAsync(url);
            VatusaUserData? userData = JsonSerializer.Deserialize<VatusaUserData>(json);
            return userData;
        }
        catch (WebException)
        {
            return null;
        }
    }
}
