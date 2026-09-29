using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FEBuddyDiscordBot.DataAccess;

namespace FEBuddyDiscordBot.Tests;

public sealed class GitHubAppAuthTests
{
    private static byte[] FromBase64Url(string text)
    {
        string padded = text.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '='));
    }

    [Fact]
    public void Jwt_is_signed_rs256_for_the_app()
    {
        using RSA key = RSA.Create(2048);
        DateTimeOffset now = DateTimeOffset.FromUnixTimeSeconds(1_800_000_000);

        string[] parts = GitHubAppAuth.CreateJwt(123456, key, now).Split('.');

        Assert.Equal(3, parts.Length);
        Assert.Equal("""{"alg":"RS256","typ":"JWT"}""", Encoding.UTF8.GetString(FromBase64Url(parts[0])));

        using JsonDocument payload = JsonDocument.Parse(FromBase64Url(parts[1]));
        Assert.Equal("123456", payload.RootElement.GetProperty("iss").GetString());
        Assert.Equal(1_800_000_000 - 60, payload.RootElement.GetProperty("iat").GetInt64());
        Assert.Equal(1_800_000_000 + 540, payload.RootElement.GetProperty("exp").GetInt64());

        Assert.True(key.VerifyData(Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}"), FromBase64Url(parts[2]),
            HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
    }
}
