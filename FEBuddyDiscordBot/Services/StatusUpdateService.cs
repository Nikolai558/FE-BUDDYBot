using FEBuddyDiscordBot.Models;

namespace FEBuddyDiscordBot.Services;

/// <summary>
/// Sends a heartbeat to an Uptime Kuma push monitor while the bot is connected to Discord.
/// If the bot is disconnected no heartbeat is sent, so Uptime Kuma reports it as down.
/// </summary>
public sealed class StatusUpdateService : BackgroundService
{
    public const string HttpClientName = "heartbeat";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly DiscordSocketClient _discord;
    private readonly HeartbeatOptions _options;
    private readonly ILogger<StatusUpdateService> _logger;

    public StatusUpdateService(IHttpClientFactory httpClientFactory, DiscordSocketClient discord, IOptions<HeartbeatOptions> options, ILogger<StatusUpdateService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _discord = discord;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled || string.IsNullOrWhiteSpace(_options.Url))
        {
            _logger.LogInformation("Heartbeat: disabled");
            return;
        }

        _logger.LogInformation("Heartbeat: sending every {Seconds}s", _options.IntervalSeconds);

        HttpClient http = _httpClientFactory.CreateClient(HttpClientName);
        using PeriodicTimer timer = new(TimeSpan.FromSeconds(_options.IntervalSeconds));

        do
        {
            if (_discord.ConnectionState != ConnectionState.Connected) continue;

            try
            {
                using HttpResponseMessage response = await http.GetAsync($"{_options.Url}?status=up&msg=OK&ping={_discord.Latency}", stoppingToken);
                if (!response.IsSuccessStatusCode) _logger.LogWarning("Heartbeat: Uptime Kuma returned HTTP {StatusCode}", (int)response.StatusCode);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !stoppingToken.IsCancellationRequested)
            {
                _logger.LogWarning("Heartbeat: {Error}", ex.Message);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
