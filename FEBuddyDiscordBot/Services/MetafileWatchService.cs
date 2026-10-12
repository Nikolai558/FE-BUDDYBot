using FEBuddyDiscordBot.Airac;
using FEBuddyDiscordBot.DataAccess;
using FEBuddyDiscordBot.Models;

namespace FEBuddyDiscordBot.Services;

/// <summary>
/// At each UTC check time (set with /admin airac-check), checks whether the FAA has published the next AIRAC cycle's d-TPP Metafile and,
/// the first time it has, pings the AIRAC pings role in its channel. Once a cycle is announced, the FAA isn't asked
/// again until that cycle's effective date, when the cycle after it becomes the one to look for.
/// </summary>
public sealed class MetafileWatchService : BackgroundService
{
    private readonly DiscordSocketClient _discord;
    private readonly DtppMetafileApi _faa;
    private readonly AiracStore _store;
    private readonly GuildSettingsStore _settings;
    private readonly ulong _guildId;
    private readonly ILogger<MetafileWatchService> _logger;

    public MetafileWatchService(
        DiscordSocketClient discord,
        DtppMetafileApi faa,
        AiracStore store,
        GuildSettingsStore settings,
        IOptions<BotOptions> options,
        ILogger<MetafileWatchService> logger)
    {
        _discord = discord;
        _faa = faa;
        _store = store;
        _settings = settings;
        _guildId = options.Value.GuildId;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Every tick, check if a check time fell since the last tick. Reading the times each tick means changes
        // made with /admin airac-check apply straight away. Times that passed while the bot was off are skipped.
        DateTimeOffset lastTick = DateTimeOffset.UtcNow;
        using PeriodicTimer timer = new(TimeSpan.FromSeconds(30));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            bool due = MetafileCheckTimes.NextAfter(lastTick, _settings.Current.AiracCheckTimesUtc) <= now;
            lastTick = now;
            if (!due) continue;

            try
            {
                await CheckAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogWarning("AIRAC: metafile check failed: {Error}", ex.ToString());
            }
        }
    }

    private async Task CheckAsync(CancellationToken cancellationToken)
    {
        AiracCycle next = AiracCycle.Next(DateOnly.FromDateTime(DateTime.UtcNow));
        if (await _store.WasAnnouncedAsync(next.Id, cancellationToken)) return;

        GuildSettings settings = _settings.Current;
        SocketGuild? guild = _discord.GetGuild(_guildId);
        if (_discord.ConnectionState != ConnectionState.Connected || guild is null)
        {
            _logger.LogInformation("AIRAC: skipped the {Cycle} metafile check; not connected to Discord", next.Id);
            return;
        }

        if (settings.AiracPingRoleId is not ulong roleId || guild.GetRole(roleId) is not { } role
            || settings.AiracPingChannelId is not ulong channelId || guild.GetTextChannel(channelId) is not { } channel)
        {
            _logger.LogInformation("AIRAC: skipped the {Cycle} metafile check; set the role and channel with /admin roles and /admin channels (airac-pings)", next.Id);
            return;
        }

        MetafileStatus status = await _faa.CheckAsync(next.Id, cancellationToken);
        if (status == MetafileStatus.NotYetPublished)
        {
            _logger.LogInformation("AIRAC: {Cycle} d-TPP Metafile not published yet", next.Id);
            return;
        }

        // Unknown was logged by the check; try again at the next check time.
        if (status != MetafileStatus.Available) return;

        DateTimeOffset found = DateTimeOffset.UtcNow;
        _logger.LogInformation("AIRAC: {Cycle} d-TPP Metafile is available (effective {Effective:yyyy-MM-dd}); pinging {Role} in #{Channel}",
            next.Id, next.EffectiveDate, role.Name, channel.Name);

        IUserMessage message = await channel.SendMessageAsync(
            MetafilePing.Text(next.Id, role.Id),
            allowedMentions: new AllowedMentions { RoleIds = [role.Id] },
            components: MetafilePing.Buttons());

        await _store.RecordAnnouncementAsync(next.Id, found, channel.Id, message.Id, cancellationToken);
    }
}
