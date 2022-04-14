using System.Net;
using System.Text.Json;
using static FEBuddyDiscordBot.Models.VatusaUserModel;

namespace FEBuddyDiscordBot.DataAccess;
public class VatusaApi
{
    public async Task<VatusaUserData?> GetVatusaUserInfo(ulong MemberId)
    {
        string url = $"https://api.vatusa.net/v2/user/{MemberId}?d";

        using (WebClient webClient = new WebClient())
        {
            try
            {
                string jsonResponse = await webClient.DownloadStringTaskAsync(url);
                VatusaUserData? userData = JsonSerializer.Deserialize<VatusaUserData>(jsonResponse);
                return userData;
            }
            catch (WebException)
            {
                return null;
            }
        }
    }
}
