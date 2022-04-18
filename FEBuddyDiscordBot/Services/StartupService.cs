﻿using System.Reflection;

namespace FEBuddyDiscordBot.Services;
public class StartupService
{
    private readonly IServiceProvider _services;
    private readonly IConfiguration _config;
    private readonly DiscordSocketClient _discord;
    private readonly CommandService _commands;
    private readonly ILogger _logger;
    private readonly DataBaseService _dataBaseService;

    public StartupService(IServiceProvider services)
    {
        _services = services;
        _config = _services.GetRequiredService<IConfiguration>();
        _discord = _services.GetRequiredService<DiscordSocketClient>();
        _commands = _services.GetRequiredService<CommandService>();
        _logger = _services.GetRequiredService<ILogger<StartupService>>();

        _dataBaseService = _services.GetRequiredService<DataBaseService>();

        _discord.Ready += DiscordReady;

        _logger.LogInformation("Loaded: StartupService");
    }

    public async Task StartAsync(bool UseDevToken)
    {
        string? token = null;

        if (UseDevToken) token = _config.GetSection("DiscordToken").GetSection("Development").Value;
        if (UseDevToken == false) token = _config.GetSection("DiscordToken").GetSection("Production").Value;

        if (string.IsNullOrEmpty(token) || string.IsNullOrWhiteSpace(token))
        {
            _logger.LogError("Token: Discord token is missing.");
            throw new Exception("Discord token is missing.");
        }

        await _discord.LoginAsync(Discord.TokenType.Bot, token);
        await _discord.StartAsync();
        await _commands.AddModulesAsync(Assembly.GetEntryAssembly(), _services);
    }

    private async Task DiscordReady()
    {
        IReadOnlyCollection<SocketGuild>? currentGuilds = _discord.Guilds;
        _dataBaseService.CheckGuilds(currentGuilds)
            .ContinueWith(t => _logger.LogWarning(t.Exception.Message), TaskContinuationOptions.OnlyOnFaulted);
    }
}
