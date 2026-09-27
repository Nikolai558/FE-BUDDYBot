using Discord.Webhook;
using FEBuddyDiscordBot.Models;

namespace FEBuddyDiscordBot.Services;

/// <summary>
/// If the bot stays disconnected from Discord for too long, send an alert and exit so Docker restarts it.
/// Short reconnects (Discord asks for these regularly) are ignored.
/// </summary>
public sealed class ConnectionWatchdog : BackgroundService
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromSeconds(5);

    private readonly DiscordSocketClient _discord;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly BotOptions _options;
    private readonly ILogger<ConnectionWatchdog> _logger;

    public ConnectionWatchdog(DiscordSocketClient discord, IHostApplicationLifetime lifetime, IOptions<BotOptions> options, ILogger<ConnectionWatchdog> logger)
    {
        _discord = discord;
        _lifetime = lifetime;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        TimeSpan timeout = TimeSpan.FromSeconds(_options.DisconnectTimeoutSeconds);
        DateTimeOffset? disconnectedSince = null;

        using PeriodicTimer timer = new(CheckInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            if (_discord.ConnectionState == ConnectionState.Connected)
            {
                disconnectedSince = null;
                continue;
            }

            disconnectedSince ??= DateTimeOffset.UtcNow;
            if (DateTimeOffset.UtcNow - disconnectedSince < timeout) continue;

            _logger.LogCritical("Watchdog: disconnected from Discord for over {Seconds}s; exiting so the container restarts.", _options.DisconnectTimeoutSeconds);
            await SendAlertAsync();

            Environment.ExitCode = 1;
            _lifetime.StopApplication();
            return;
        }
    }

    private async Task SendAlertAsync()
    {
        if (string.IsNullOrWhiteSpace(_options.DisconnectWebhookUrl)) return;

        try
        {
            using DiscordWebhookClient webhook = new(_options.DisconnectWebhookUrl);
            await webhook.SendMessageAsync($"FE-Buddy bot has been disconnected from Discord for over {_options.DisconnectTimeoutSeconds} seconds and is restarting.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Watchdog: could not send webhook alert: {Error}", ex.Message);
        }
    }
}
