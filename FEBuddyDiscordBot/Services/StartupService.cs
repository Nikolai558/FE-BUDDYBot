using Discord.Interactions;
using System.Reflection;

namespace FEBuddyDiscordBot.Services;

/// <summary>
/// Startup service for discord gateway; log the bot into discord and add event handlers.
/// </summary>
public class StartupService
{
    // Dependency Injection services needed 
    private readonly IServiceProvider _services;
    private readonly IConfiguration _config;
    private readonly DiscordSocketClient _discord;
    private readonly CommandService _commands;
    private readonly ILogger _logger;
    private readonly InteractionService _interactionService;
    private readonly DataBaseService _dataBaseService;

    private int _disconnectCount = 0;
    private int _disconnectLimit = 1;

    /// <summary>
    /// Constructor for the Bot Startup Service
    /// </summary>
    /// <param name="services">Dependency Injection service provider.</param>
    public StartupService(IServiceProvider services)
    {
        _services = services;
        _config = _services.GetRequiredService<IConfiguration>();
        _discord = _services.GetRequiredService<DiscordSocketClient>();
        _commands = _services.GetRequiredService<CommandService>();
        _logger = _services.GetRequiredService<ILogger<StartupService>>();
        _interactionService = _services.GetRequiredService<InteractionService>();
        _dataBaseService = _services.GetRequiredService<DataBaseService>();

        _discord.Ready += DiscordReady;
        _discord.JoinedGuild += JoinedNewGuild;

        _discord.Disconnected += BotDisconected;

        _logger.LogDebug("Loaded: StartupService");
    }

    private Task BotDisconected(Exception arg)
    {
        _disconnectCount += 1;

        if (_disconnectCount > _disconnectLimit)
        {
            return Task.CompletedTask;
        }

        _logger.LogInformation($"Gateway Disconnect: Bot disconecting/disconnected {_disconnectCount} of {_disconnectLimit}");
        if (_discord.ConnectionState == ConnectionState.Disconnecting || _discord.ConnectionState == ConnectionState.Disconnected)
        {
            if (_disconnectCount == _disconnectLimit)
            {
                var closeApplicationTask = Task.Run(async () =>
                {
                    _logger.LogWarning($"Gateway Disconnect: Bot disconected. App will exit in 30 seconds if not reconnected by that time.");
                    Thread.Sleep(30000);
                    if (_discord.ConnectionState == ConnectionState.Disconnecting || _discord.ConnectionState == ConnectionState.Disconnected)
                    {
                        _logger.LogCritical($"Gateway Disconnect: Bot disconected for longer than 30 seconds, closing application.");
                        await _discord.LogoutAsync();
                        Environment.Exit(1);
                    }
                    else
                    {
                        _logger.LogInformation($"Gateway Disconnect: Gateway status - {_discord.ConnectionState}, not exiting application.");
                        _disconnectCount = 0;
                    }
                });
            }
        }

        return Task.CompletedTask;
    }

    private async Task JoinedNewGuild(SocketGuild arg)
    {
#pragma warning disable CS4014
        _dataBaseService.CheckGuilds(new List<SocketGuild>() { arg })
            .ContinueWith(t => _logger.LogWarning(t.Exception?.Message), TaskContinuationOptions.OnlyOnFaulted);
#pragma warning restore CS4014

        await _interactionService.RegisterCommandsToGuildAsync(arg.Id);
    }

    /// <summary>
    /// Start the Discord bot, log into discord, monitor discord events.
    /// </summary>
    /// <param name="UseDevToken">Use the developer bot token instead of the production bot token.</param>
    /// <returns>None</returns>
    /// <exception cref="Exception">Throws an exception if the token is null, empty, or whitespace.</exception>
    public async Task StartAsync(bool UseDevToken)
    {
        string? token = null;

        // Grab the bot token
        if (UseDevToken) token = _config.GetSection("DiscordToken").GetSection("Development").Value;
        if (UseDevToken == false) token = _config.GetSection("DiscordToken").GetSection("Production").Value;

        // Check to make sure the token is not null, empty, or whitespace.
        if (string.IsNullOrEmpty(token) || string.IsNullOrWhiteSpace(token))
        {
            _logger.LogError("Token: Discord token is missing.");
            throw new Exception("Discord token is missing.");
        }

        await _discord.LoginAsync(Discord.TokenType.Bot, token);
        await _discord.StartAsync();
        await _commands.AddModulesAsync(Assembly.GetEntryAssembly(), _services);
    }

    /// <summary>
    /// Handle Discord.Ready events
    /// </summary>
    /// <returns>None</returns>
    private async Task DiscordReady()
    {
        _disconnectCount = 0;

        // Get a list of guilds (discord servers) the bot is currently in.
        List<SocketGuild>? currentGuilds = _discord.Guilds.ToList();

        // For development only! For production use await _interactionService.RegisterCommandsGloballyAsync(); instead of foreach loop.
        foreach (var guild in currentGuilds)
        {
            // Register any slash commands to the individual discord server
            await _interactionService.RegisterCommandsToGuildAsync(guild.Id);
        }

#pragma warning disable CS4014 // Don't want to await this because it will block the discord gateway tasks. We only want to log any errors that come with it
        // Check the database to make sure all current guilds have at least the default configurations set.
        _dataBaseService.CheckGuilds(currentGuilds)
            .ContinueWith(t => _logger.LogWarning(t.Exception?.Message), TaskContinuationOptions.OnlyOnFaulted);
#pragma warning restore CS4014

    }
}
