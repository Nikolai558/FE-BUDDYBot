using System.Text;
using System.Text.RegularExpressions;
using FEBuddyDiscordBot.DataAccess;
using FEBuddyDiscordBot.Models;
using Serilog.Events;

namespace FEBuddyDiscordBot.Services;

/// <summary>
/// Posts the bot's logs in the private log channel set with /admin logs, and pings the alert role when something goes wrong.
/// Information and above is posted; Error and Critical ping the alert role, at most once per kind of error every
/// <see cref="AlertCooldown"/>. Logs wait (up to <see cref="DiscordLogSink"/>'s capacity) while the bot is disconnected.
/// Debug logs and stack traces stay in the console and the log files.
/// </summary>
public sealed partial class DiscordLogService : BackgroundService
{
    internal static readonly TimeSpan AlertCooldown = TimeSpan.FromMinutes(30);

    /// <summary>How long to let a burst of logs (e.g. a /check-users run) collect into one message.</summary>
    private static readonly TimeSpan BatchDelay = TimeSpan.FromSeconds(3);

    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(10);
    private const int MaxEntriesPerBatch = 50;

    private readonly DiscordLogSink _sink;
    private readonly DiscordSocketClient _discord;
    private readonly GuildSettingsStore _settings;
    private readonly ulong _guildId;
    private readonly string[] _secrets;
    private readonly ILogger<DiscordLogService> _logger;

    /// <summary>When each kind of error last pinged the alert role.</summary>
    private readonly Dictionary<string, DateTimeOffset> _lastAlerts = [];

    private bool _warnedChannelMissing;

    public DiscordLogService(DiscordLogSink sink, DiscordSocketClient discord, GuildSettingsStore settings,
        IOptions<BotOptions> options, IOptions<HeartbeatOptions> heartbeat, ILogger<DiscordLogService> logger)
    {
        _sink = sink;
        _discord = discord;
        _settings = settings;
        _guildId = options.Value.GuildId;
        _secrets = new[] { options.Value.Token, options.Value.DisconnectWebhookUrl, heartbeat.Value.Url }
            .Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s!).ToArray();
        _logger = logger;
    }

    /// <summary>
    /// Which log events go to the channel. Discord.Net's own Information logs (routine reconnects) are left out, and so are
    /// this service's: a failure to post would otherwise loop back into the channel.
    /// </summary>
    internal static bool ShouldPost(LogEventLevel level, string source) =>
        source != typeof(DiscordLogService).FullName
        && level >= (source == typeof(LoggingService).FullName ? LogEventLevel.Warning : LogEventLevel.Information);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            while (await _sink.Reader.WaitToReadAsync(stoppingToken))
            {
                await Task.Delay(BatchDelay, stoppingToken);

                if (_settings.Current.LogChannelId is not ulong channelId)
                {
                    Discard();
                    continue;
                }

                if (_discord.ConnectionState != ConnectionState.Connected || _discord.GetGuild(_guildId) is not { IsConnected: true } guild)
                {
                    await Task.Delay(RetryDelay, stoppingToken);
                    continue;
                }

                if (guild.GetTextChannel(channelId) is not { } channel)
                {
                    if (!_warnedChannelMissing) _logger.LogWarning("Bot Logs: log channel {ChannelId} no longer exists; set it again with /admin logs", channelId);
                    _warnedChannelMissing = true;
                    Discard();
                    continue;
                }

                _warnedChannelMissing = false;
                await PostAsync(channel, TakeBatch(), extraLine: null, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            await FlushOnShutdownAsync();
        }
    }

    /// <summary>
    /// Post whatever is left and say the bot is stopping, so a deploy shows up as "shutting down" then "online".
    /// Hosted services stop in reverse order and this one is registered last, so Discord is still connected here.
    /// </summary>
    private async Task FlushOnShutdownAsync()
    {
        if (_settings.Current.LogChannelId is not ulong channelId || _discord.ConnectionState != ConnectionState.Connected
            || _discord.GetGuild(_guildId)?.GetTextChannel(channelId) is not { } channel)
        {
            return;
        }

        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));
        try
        {
            await PostAsync(channel, TakeBatch(int.MaxValue), extraLine: "⏹️ The bot is shutting down.", timeout.Token);
        }
        catch (OperationCanceledException)
        {
            // Out of time; the rest is in the log files.
        }
    }

    private List<DiscordLogEntry> TakeBatch(int max = MaxEntriesPerBatch)
    {
        List<DiscordLogEntry> batch = [];
        while (batch.Count < max && _sink.Reader.TryRead(out DiscordLogEntry? entry)) batch.Add(entry);
        return batch;
    }

    private void Discard()
    {
        while (_sink.Reader.TryRead(out _)) { }
        _sink.TakeDroppedCount();
    }

    private async Task PostAsync(SocketTextChannel channel, List<DiscordLogEntry> batch, string? extraLine, CancellationToken cancellationToken)
    {
        GuildSettings settings = _settings.Current;
        DateTimeOffset now = DateTimeOffset.UtcNow;

        List<string> lines = FormatLines(batch, _secrets);
        if (_sink.TakeDroppedCount() is int dropped and > 0)
        {
            lines.Insert(0, $"⚠️ {dropped} older log lines were dropped while they couldn't be posted. They're in the log files.");
        }
        if (extraLine is not null) lines.Add(extraLine);
        if (lines.Count == 0) return;

        // Check every entry, so each kind of error's cooldown starts even when another one already triggers the ping.
        bool alert = false;
        foreach (DiscordLogEntry entry in batch) alert |= ShouldAlert(_lastAlerts, entry, now);

        AllowedMentions mentions = AllowedMentions.None;
        if (alert && settings.AlertRoleId is ulong roleId)
        {
            lines.Insert(0, $"<@&{roleId}> 🚨 Something went wrong:");
            mentions = new AllowedMentions { RoleIds = [roleId] };
        }

        foreach (string message in Chunk(lines, DiscordConfig.MaxMessageSize))
        {
            try
            {
                await channel.SendMessageAsync(message, allowedMentions: mentions, flags: MessageFlags.SuppressEmbeds,
                    options: new RequestOptions { CancelToken = cancellationToken });
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning("Bot Logs: could not post in #{Channel}: {Error}", channel.Name, ex.Message);
                return;
            }

            // Only the first message pings.
            mentions = AllowedMentions.None;
        }
    }

    /// <summary>
    /// True if this entry should ping the alert role: an Error or Critical whose kind (source and message template) hasn't
    /// pinged in the last <see cref="AlertCooldown"/>. Records the ping in <paramref name="lastAlerts"/>.
    /// </summary>
    internal static bool ShouldAlert(Dictionary<string, DateTimeOffset> lastAlerts, DiscordLogEntry entry, DateTimeOffset now)
    {
        if (entry.Level < LogEventLevel.Error) return false;

        string key = entry.Source + "|" + entry.Template;
        if (lastAlerts.TryGetValue(key, out DateTimeOffset last) && now - last < AlertCooldown) return false;

        lastAlerts[key] = now;
        return true;
    }

    /// <summary>One line per entry; repeats of the same entry in a row become one line with a count.</summary>
    internal static List<string> FormatLines(IReadOnlyList<DiscordLogEntry> entries, IReadOnlyList<string> secrets)
    {
        List<string> lines = [];
        for (int i = 0; i < entries.Count;)
        {
            DiscordLogEntry entry = entries[i];
            int repeats = 1;
            while (i + repeats < entries.Count && entries[i + repeats] is var next
                && next.Level == entry.Level && next.Text == entry.Text && next.Exception == entry.Exception)
            {
                repeats++;
            }

            lines.Add(FormatLine(entry, repeats, secrets));
            i += repeats;
        }

        return lines;
    }

    // e.g. "▫️ 14:03:22 **Command** nikolai558 (353684697651478528) ran /admin settings", with the time in the reader's time zone.
    internal static string FormatLine(DiscordLogEntry entry, int repeats, IReadOnlyList<string> secrets)
    {
        string icon = entry.Level switch
        {
            LogEventLevel.Fatal => "🛑",
            LogEventLevel.Error => "❌",
            LogEventLevel.Warning => "⚠️",
            _ => "▫️",
        };

        // Every log message starts with a short category, e.g. "Command: ..." or "AIRAC: ...". Show it in bold.
        string text = Redact(entry.Text, secrets);
        int colon = text.IndexOf(": ", StringComparison.Ordinal);
        string message = colon is > 0 and <= 25
            ? $"**{Format.Sanitize(text[..colon])}** {Format.Sanitize(text[(colon + 2)..])}"
            : Format.Sanitize(text);

        StringBuilder line = new($"{icon} <t:{entry.Timestamp.ToUnixTimeSeconds()}:T> {message}");
        if (repeats > 1) line.Append($" (×{repeats})");
        if (entry.Exception is not null) line.Append($"\n-# ↳ {Format.Sanitize(Redact(entry.Exception, secrets))}");
        return line.ToString();
    }

    /// <summary>Hide the bot's own secrets and anything shaped like a webhook URL or GitHub token.</summary>
    internal static string Redact(string text, IReadOnlyList<string> secrets)
    {
        foreach (string secret in secrets)
        {
            if (secret.Length >= 8) text = text.Replace(secret, "[redacted]", StringComparison.Ordinal);
        }

        return SecretPattern().Replace(text, "[redacted]");
    }

    [GeneratedRegex(@"https://(?:\w+\.)?discord(?:app)?\.com/api/(?:v\d+/)?webhooks/\d+/[\w-]+|\bgh[pousr]_[A-Za-z0-9]{20,}|\bgithub_pat_\w{20,}")]
    private static partial Regex SecretPattern();

    /// <summary>Join lines into as few messages as fit in <paramref name="maxLength"/>. A line too long on its own is cut.</summary>
    internal static List<string> Chunk(IEnumerable<string> lines, int maxLength)
    {
        List<string> messages = [];
        StringBuilder current = new();

        foreach (string raw in lines)
        {
            string line = raw.Length > maxLength ? raw[..(maxLength - 1)] + "…" : raw;
            if (current.Length > 0 && current.Length + 1 + line.Length > maxLength)
            {
                messages.Add(current.ToString());
                current.Clear();
            }

            if (current.Length > 0) current.Append('\n');
            current.Append(line);
        }

        if (current.Length > 0) messages.Add(current.ToString());
        return messages;
    }
}
