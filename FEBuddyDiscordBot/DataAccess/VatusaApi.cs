using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FEBuddyDiscordBot.Models;

namespace FEBuddyDiscordBot.DataAccess;

public enum VatusaLookupStatus
{
    /// <summary>The Discord account is linked to a VATUSA account.</summary>
    Found,

    /// <summary>VATUSA has no account linked to this Discord account.</summary>
    NotLinked,

    /// <summary>VATUSA could not be reached or returned an error. Try again later.</summary>
    Unavailable,
}

public sealed record VatusaLookup(VatusaLookupStatus Status, VatusaUser? User = null);

/// <summary>
/// Access to the VATUSA API (the legacy v2 endpoints, still the only public way to look a user up by Discord ID).
/// </summary>
public sealed class VatusaApi
{
    public const string HttpClientName = "vatusa";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<VatusaApi> _logger;

    public VatusaApi(IHttpClientFactory httpClientFactory, ILogger<VatusaApi> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    /// <summary>
    /// Look up a VATUSA member by their Discord user ID.
    /// </summary>
    public async Task<VatusaLookup> GetUserByDiscordIdAsync(ulong discordId, CancellationToken cancellationToken = default)
    {
        HttpClient http = _httpClientFactory.CreateClient(HttpClientName);

        try
        {
            using HttpResponseMessage response = await http.GetAsync($"v2/user/{discordId}?d", cancellationToken);

            // VATUSA answers 404 when no account is linked to this Discord ID.
            if (response.StatusCode == HttpStatusCode.NotFound) return new(VatusaLookupStatus.NotLinked);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("VATUSA: lookup for {DiscordId} failed with HTTP {StatusCode}", discordId, (int)response.StatusCode);
                return new(VatusaLookupStatus.Unavailable);
            }

            VatusaUserResponse? body = await response.Content.ReadFromJsonAsync<VatusaUserResponse>(cancellationToken);
            if (body?.Data?.Cid is null) return new(VatusaLookupStatus.NotLinked);

            return new(VatusaLookupStatus.Found, body.Data);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException
                                   || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            _logger.LogWarning("VATUSA: lookup for {DiscordId} failed: {Error}", discordId, ex.Message);
            return new(VatusaLookupStatus.Unavailable);
        }
    }
}
