using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FEBuddyDiscordBot.Models;

namespace FEBuddyDiscordBot.DataAccess;

/// <summary>
/// Signs in as the GitHub App and hands out installation access tokens for the configured repository.
/// Tokens last an hour; a cached one is reused until five minutes before it expires.
/// </summary>
public sealed class GitHubAppAuth
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly GitHubOptions _options;
    private readonly string _contentRoot;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private RSA? _key;
    private long? _installationId;
    private string? _token;
    private DateTimeOffset _tokenExpires;

    public GitHubAppAuth(IHttpClientFactory httpClientFactory, IOptions<GitHubOptions> options, IHostEnvironment environment)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _contentRoot = environment.ContentRootPath;
    }

    /// <summary>An installation token for the repository, fetching a new one when needed.</summary>
    public async Task<string> GetTokenAsync(CancellationToken cancellationToken = default)
    {
        if (_token is not null && DateTimeOffset.UtcNow < _tokenExpires.AddMinutes(-5)) return _token;

        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_token is not null && DateTimeOffset.UtcNow < _tokenExpires.AddMinutes(-5)) return _token;

            HttpClient http = _httpClientFactory.CreateClient(GitHubApi.HttpClientName);
            string jwt = CreateJwt(_options.AppId, LoadKey(), DateTimeOffset.UtcNow);

            _installationId ??= (await SendAsync<InstallationResponse>(http, HttpMethod.Get, $"repos/{_options.Repository}/installation", jwt, cancellationToken)).Id;

            TokenResponse token = await SendAsync<TokenResponse>(http, HttpMethod.Post, $"app/installations/{_installationId}/access_tokens", jwt, cancellationToken);
            _token = token.Token;
            _tokenExpires = token.ExpiresAt;
            return _token;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// A GitHub App JSON Web Token (RS256), valid for 9 minutes. Backdated a minute to allow for clock drift.
    /// </summary>
    internal static string CreateJwt(long appId, RSA key, DateTimeOffset now)
    {
        static string Base64Url(ReadOnlySpan<byte> bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        string header = Base64Url("""{"alg":"RS256","typ":"JWT"}"""u8);
        string payload = Base64Url(JsonSerializer.SerializeToUtf8Bytes(new
        {
            iat = now.AddSeconds(-60).ToUnixTimeSeconds(),
            exp = now.AddMinutes(9).ToUnixTimeSeconds(),
            iss = appId.ToString(System.Globalization.CultureInfo.InvariantCulture),
        }));

        byte[] signature = key.SignData(Encoding.ASCII.GetBytes($"{header}.{payload}"), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return $"{header}.{payload}.{Base64Url(signature)}";
    }

    private RSA LoadKey()
    {
        if (_key is not null) return _key;

        string path = Path.Combine(_contentRoot, _options.PrivateKeyPath!);
        RSA key = RSA.Create();
        try
        {
            key.ImportFromPem(File.ReadAllText(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or CryptographicException)
        {
            key.Dispose();
            throw new GitHubException($"Couldn't read the GitHub App private key at {path}: {ex.Message}");
        }

        return _key = key;
    }

    private static async Task<T> SendAsync<T>(HttpClient http, HttpMethod method, string url, string jwt, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", jwt);

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            throw new GitHubException($"GitHub could not be reached to sign in: {ex.Message}");
        }

        using HttpResponseMessage _ = response;
        if (!response.IsSuccessStatusCode)
        {
            throw new GitHubException($"GitHub App sign-in failed at {url}: HTTP {(int)response.StatusCode}. Check GitHub:AppId, the private key, and that the app is installed on the repository.");
        }

        return (await response.Content.ReadFromJsonAsync<T>(cancellationToken))!;
    }

    private sealed record InstallationResponse([property: JsonPropertyName("id")] long Id);

    private sealed record TokenResponse(
        [property: JsonPropertyName("token")] string Token,
        [property: JsonPropertyName("expires_at")] DateTimeOffset ExpiresAt);
}
