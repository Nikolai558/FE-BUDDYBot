using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using FEBuddyDiscordBot.Models;

namespace FEBuddyDiscordBot.DataAccess;

/// <param name="UserCode">What the member types at <paramref name="VerificationUri"/>, e.g. "WDJB-MJHT".</param>
public sealed record DeviceCode(
    [property: JsonPropertyName("device_code")] string Code,
    [property: JsonPropertyName("user_code")] string UserCode,
    [property: JsonPropertyName("verification_uri")] string VerificationUri,
    [property: JsonPropertyName("expires_in")] int ExpiresIn,
    [property: JsonPropertyName("interval")] int Interval);

/// <summary>
/// GitHub's device flow, used only to learn which GitHub account a member has: they type a code on github.com,
/// GitHub tells the bot who they are, and the bot throws the resulting token away.
/// </summary>
public sealed class GitHubDeviceFlow
{
    public const string HttpClientName = "github-web";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly GitHubOptions _options;

    public GitHubDeviceFlow(IHttpClientFactory httpClientFactory, IOptions<GitHubOptions> options)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.ClientId);

    public async Task<DeviceCode> StartAsync(CancellationToken cancellationToken = default)
    {
        HttpClient http = _httpClientFactory.CreateClient(HttpClientName);
        using HttpResponseMessage response = await PostAsync(http, "login/device/code", new() { ["client_id"] = _options.ClientId! }, cancellationToken);
        if (!response.IsSuccessStatusCode) throw new GitHubException($"GitHub refused to start the device flow: HTTP {(int)response.StatusCode}");
        return (await response.Content.ReadFromJsonAsync<DeviceCode>(cancellationToken))!;
    }

    /// <summary>
    /// Wait for the member to enter the code. Returns their GitHub login, or null if they declined or the code expired.
    /// </summary>
    public async Task<string?> WaitForLoginAsync(DeviceCode code, CancellationToken cancellationToken = default)
    {
        HttpClient http = _httpClientFactory.CreateClient(HttpClientName);
        DateTimeOffset expires = DateTimeOffset.UtcNow.AddSeconds(code.ExpiresIn);
        TimeSpan interval = TimeSpan.FromSeconds(Math.Max(5, code.Interval));

        while (DateTimeOffset.UtcNow < expires)
        {
            await Task.Delay(interval, cancellationToken);

            TokenResponse? token;
            try
            {
                using HttpResponseMessage response = await PostAsync(http, "login/oauth/access_token", new()
                {
                    ["client_id"] = _options.ClientId!,
                    ["device_code"] = code.Code,
                    ["grant_type"] = "urn:ietf:params:oauth:grant-type:device_code",
                }, cancellationToken);
                token = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken);
            }
            catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException
                                       || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
            {
                // A hiccup (timeout, GitHub error page): the member may still approve, so keep waiting.
                continue;
            }

            switch (token?.Error)
            {
                case null when token?.AccessToken is string accessToken:
                    return await GetLoginAsync(accessToken, cancellationToken);
                case "authorization_pending":
                    continue;
                case "slow_down":
                    interval += TimeSpan.FromSeconds(5);
                    continue;
                default:
                    // expired_token, access_denied, or something unexpected.
                    return null;
            }
        }

        return null;
    }

    private async Task<string> GetLoginAsync(string accessToken, CancellationToken cancellationToken)
    {
        HttpClient api = _httpClientFactory.CreateClient(GitHubApi.HttpClientName);
        using HttpRequestMessage request = new(HttpMethod.Get, "user");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        using HttpResponseMessage response = await api.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode) throw new GitHubException($"GitHub didn't say who signed in: HTTP {(int)response.StatusCode}");
        return (await response.Content.ReadFromJsonAsync<GitHubUser>(cancellationToken))!.Login;
    }

    private static Task<HttpResponseMessage> PostAsync(HttpClient http, string url, Dictionary<string, string> form, CancellationToken cancellationToken)
    {
        HttpRequestMessage request = new(HttpMethod.Post, url) { Content = new FormUrlEncodedContent(form) };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return http.SendAsync(request, cancellationToken);
    }

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string? AccessToken,
        [property: JsonPropertyName("error")] string? Error);
}
