using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;

namespace FEBuddyDiscordBot.DataAccess;

public enum MetafileStatus
{
    /// <summary>The FAA has published the metafile for the cycle.</summary>
    Available,

    /// <summary>The FAA answered 404: not published yet.</summary>
    NotYetPublished,

    /// <summary>The FAA couldn't be reached, or answered with something other than the metafile. Try again later.</summary>
    Unknown,
}

/// <summary>
/// Checks whether the FAA has published a cycle's d-TPP Metafile (d-tpp_Metafile.xml), the file FE-BUDDY needs for a
/// full AIRAC export. The FAA usually publishes it 15-18 days before the cycle's effective date.
/// </summary>
public sealed partial class DtppMetafileApi
{
    public const string HttpClientName = "faa-dtpp";

    // The file is about 16 MB. Its root element, which names the cycle, is in the first couple of hundred bytes.
    private const int BytesToRead = 1024;

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<DtppMetafileApi> _logger;

    public DtppMetafileApi(IHttpClientFactory httpClientFactory, ILogger<DtppMetafileApi> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public static string UrlFor(string cycleId) => $"https://aeronav.faa.gov/d-tpp/{cycleId}/xml_data/d-tpp_Metafile.xml";

    public async Task<MetafileStatus> CheckAsync(string cycleId, CancellationToken cancellationToken = default)
    {
        HttpClient http = _httpClientFactory.CreateClient(HttpClientName);
        using HttpRequestMessage request = new(HttpMethod.Get, UrlFor(cycleId));
        request.Headers.Range = new RangeHeaderValue(0, BytesToRead - 1);

        try
        {
            using HttpResponseMessage response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

            if (response.StatusCode == HttpStatusCode.NotFound) return MetafileStatus.NotYetPublished;

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("AIRAC: metafile check for {Cycle} failed with HTTP {StatusCode}", cycleId, (int)response.StatusCode);
                return MetafileStatus.Unknown;
            }

            // A 200 isn't enough: check it's really the metafile for this cycle, not an error page or another cycle's file.
            string start = await ReadStartAsync(response, cancellationToken);
            string? cycle = CycleIn(start);
            if (cycle == cycleId) return MetafileStatus.Available;

            _logger.LogWarning("AIRAC: metafile check for {Cycle} got HTTP {StatusCode} but {Found}", cycleId, (int)response.StatusCode,
                cycle is null ? "not a d-TPP Metafile" : $"the metafile for cycle {cycle}");
            return MetafileStatus.Unknown;
        }
        catch (Exception ex) when (ex is HttpRequestException
                                   || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            _logger.LogWarning("AIRAC: metafile check for {Cycle} failed: {Error}", cycleId, ex.Message);
            return MetafileStatus.Unknown;
        }
    }

    /// <summary>The cycle named by the metafile's root element, e.g. 2611 from &lt;digital_tpp cycle="2611" ...&gt;.</summary>
    internal static string? CycleIn(string start) => RootCycle().Match(start) is { Success: true } match ? match.Groups[1].Value : null;

    // The server may ignore the Range header and send the whole file, so stop reading after the first few bytes.
    private static async Task<string> ReadStartAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        byte[] buffer = new byte[BytesToRead];
        int total = 0;
        while (total < buffer.Length && await stream.ReadAsync(buffer.AsMemory(total), cancellationToken) is int read and > 0)
        {
            total += read;
        }

        return Encoding.UTF8.GetString(buffer, 0, total);
    }

    [GeneratedRegex(@"<digital_tpp\b[^>]*\bcycle=""([^""]*)""")]
    private static partial Regex RootCycle();
}
