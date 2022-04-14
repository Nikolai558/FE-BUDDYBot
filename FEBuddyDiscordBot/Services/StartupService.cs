using System.Reflection;

namespace FEBuddyDiscordBot.Services;
public class StartupService
{
    private readonly IServiceProvider _services;
    private readonly IConfigurationRoot _config;
    private readonly DiscordSocketClient _discord;
    private readonly CommandService _commands;
    private readonly ILogger _logger;

    public StartupService(IServiceProvider services)
    {
        _services = services;
        _config = _services.GetRequiredService<IConfigurationRoot>();
        _discord = _services.GetRequiredService<DiscordSocketClient>();
        _commands = _services.GetRequiredService<CommandService>();
        _logger = _services.GetRequiredService<ILogger<StartupService>>();

        _logger.LogInformation("Loaded: StartupService");
    }

    public async Task StartAsync()
    {
        string token = _config["Token"];

        if (string.IsNullOrEmpty(token) || string.IsNullOrWhiteSpace(token))
        {
            _logger.LogError("Token: Discord token is missing.");
            throw new Exception("Discord token is missing.");
        }

        await _discord.LoginAsync(Discord.TokenType.Bot, token);
        await _discord.StartAsync();
        await _commands.AddModulesAsync(Assembly.GetEntryAssembly(), _services);
    }
}
