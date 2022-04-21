using System.Net;
using System.Text.Json;
using static FEBuddyDiscordBot.Models.VatusaUserModel;

namespace FEBuddyDiscordBot.DataAccess;
public class VatusaApi
{
    public async Task<VatusaUserData?> GetVatusaUserInfo(ulong MemberId)
    {
        string url = $"https://api.vatusa.net/v2/user/{MemberId}?d";

        using (HttpClient httpClient = new HttpClient())
        {
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
}
