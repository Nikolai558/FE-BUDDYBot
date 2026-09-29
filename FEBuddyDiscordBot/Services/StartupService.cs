using FEBuddyDiscordBot.DataAccess;
using FEBuddyDiscordBot.Models;

namespace FEBuddyDiscordBot.Services;

/// <summary>
/// Logs the bot in, registers slash commands, and seeds the server settings on first run.
/// </summary>
public sealed class StartupService : IHostedService
{
    private readonly DiscordSocketClient _discord;
    private readonly InteractionService _interactions;
    private readonly InteractionHandler _interactionHandler;
    private readonly GuildSettingsStore _settings;
    private readonly IssueStore _issueStore;
    private readonly GuildDefaultsOptions _defaults;
    private readonly BotOptions _options;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly ILogger<StartupService> _logger;

    private bool _firstReadyHandled;

    public StartupService(
        DiscordSocketClient discord,
        InteractionService interactions,
        InteractionHandler interactionHandler,
        GuildSettingsStore settings,
        IssueStore issueStore,
        IOptions<GuildDefaultsOptions> defaults,
        IOptions<BotOptions> options,
        IHostApplicationLifetime lifetime,
        ILogger<StartupService> logger,
        // Resolved here only so they subscribe to Discord events before the bot connects.
        LoggingService loggingService,
        RoleAssignmentService roleAssignmentService)
    {
        _discord = discord;
        _interactions = interactions;
        _interactionHandler = interactionHandler;
        _settings = settings;
        _issueStore = issueStore;
        _defaults = defaults.Value;
        _options = options.Value;
        _lifetime = lifetime;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _settings.InitializeAsync(cancellationToken);
        await _issueStore.InitializeAsync(cancellationToken);
        await _interactionHandler.InitializeAsync();

        _discord.Ready += OnReadyAsync;
        _discord.Disconnected += OnDisconnectedAsync;

        await _discord.LoginAsync(TokenType.Bot, _options.Token);
        await _discord.StartAsync();
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await _discord.StopAsync();
        await _discord.LogoutAsync();
    }

    // Discord.Net retries forever on a rejected token; stop instead so the problem is obvious.
    private Task OnDisconnectedAsync(Exception exception)
    {
        if (exception is Discord.Net.HttpException { HttpCode: System.Net.HttpStatusCode.Unauthorized })
        {
            _logger.LogCritical("Startup: Discord rejected the bot token. Check Bot:Token.");
            Environment.ExitCode = 1;
            _lifetime.StopApplication();
        }

        return Task.CompletedTask;
    }

    // Ready fires again after every full reconnect; only do the one-time setup once.
    private async Task OnReadyAsync()
    {
        if (_firstReadyHandled) return;
        _firstReadyHandled = true;

        SocketGuild? guild = _discord.GetGuild(_options.GuildId);
        if (guild is null)
        {
            _logger.LogCritical("Startup: the bot is not a member of the configured server {GuildId}. Check Bot:GuildId.", _options.GuildId);
            return;
        }

        foreach (SocketGuild other in _discord.Guilds.Where(g => g.Id != guild.Id))
        {
            _logger.LogWarning("Startup: the bot is also in {GuildName} ({GuildId}), which it does not serve.", other.Name, other.Id);
        }

        await _interactions.RegisterCommandsToGuildAsync(guild.Id, deleteMissing: true);
        _logger.LogInformation("Startup: registered slash commands in {GuildName}", guild.Name);

        if (!_settings.IsSeeded) await SeedSettingsAsync(guild);

        if (!string.IsNullOrWhiteSpace(_options.Status)) await _discord.SetCustomStatusAsync(_options.Status);
    }

    /// <summary>
    /// First run with an empty database: copy GuildDefaults from configuration, resolving role and channel names to IDs.
    /// </summary>
    private async Task SeedSettingsAsync(SocketGuild guild)
    {
        ulong? Role(string? name, string label)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            SocketRole? role = guild.Roles.FirstOrDefault(r => r.Name == name);
            if (role is null) _logger.LogWarning("Database: {Label} role '{Name}' was not found in {Guild}; set it with /admin roles", label, name, guild.Name);
            return role?.Id;
        }

        ulong? Channel(string? name, string label)
        {
            if (string.IsNullOrWhiteSpace(name)) return null;
            SocketGuildChannel? channel = guild.Channels.FirstOrDefault(c => c.Name == name);
            if (channel is null) _logger.LogWarning("Database: {Label} channel '{Name}' was not found in {Guild}; set it with /admin channels", label, name, guild.Name);
            return channel?.Id;
        }

        await _settings.UpdateAsync(s =>
        {
            s.AssignRolesOnJoin = _defaults.AssignRolesOnJoin;
            s.AssignRolesOnVoiceJoin = _defaults.AssignRolesOnVoiceJoin;
            s.PrivateMeetingRoleEnabled = _defaults.PrivateMeetingRoleEnabled;
            s.ChangeNicknames = _defaults.ChangeNicknames;
            s.AssignStaffRole = _defaults.AssignStaffRole;

            s.VerifiedRoleId = Role(_defaults.VerifiedRoleName, "Verified");
            s.StaffRoleId = Role(_defaults.StaffRoleName, "Staff");
            s.PrivateMeetingRoleId = Role(_defaults.PrivateMeetingRoleName, "Private meeting");
            s.PrivateMeetingChannelId = Channel(_defaults.PrivateMeetingChannelName, "Private meeting");
            s.RolesChannelId = Channel(_defaults.RolesChannelName, "Roles");
        });

        _logger.LogInformation("Database: seeded settings for {Guild} from GuildDefaults", guild.Name);
    }
}
