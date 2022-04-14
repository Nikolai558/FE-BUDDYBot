using FEBuddyDiscordBot.DataAccess.DB;
using FEBuddyDiscordBot.Models;
using System.Reflection;

namespace FEBuddyDiscordBot.Services;
public class StartupService
{
    private readonly IServiceProvider _services;
    private readonly IConfiguration _config;
    private readonly DiscordSocketClient _discord;
    private readonly CommandService _commands;
    private readonly ILogger _logger;
    private IMongoGuildData _guildData;

    public StartupService(IServiceProvider services)
    {
        _services = services;
        _config = _services.GetRequiredService<IConfiguration>();
        _discord = _services.GetRequiredService<DiscordSocketClient>();
        _commands = _services.GetRequiredService<CommandService>();
        _logger = _services.GetRequiredService<ILogger<StartupService>>();

        _guildData = _services.GetRequiredService<IMongoGuildData>();

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

    private Task DiscordReady()
    {
        IReadOnlyCollection<SocketGuild>? currentGuilds = _discord.Guilds;
        CheckGuildDB(currentGuilds);
        return Task.CompletedTask;
    }

    public async Task CheckGuildDB(IReadOnlyCollection<SocketGuild> guildList)
    {
        foreach (var guild in guildList)
        {
            GuildModel foundGuildInDb = await _guildData.GetGuildAsync(guild.Id.ToString());
            if (foundGuildInDb != null)
            {
                _logger.LogInformation("TEST: Found Guild in DB");
            }
            else
            {
                _logger.LogInformation("TEST: Did NOT find Guild in DB");

                GuildModel newGuild = new GuildModel
                {
                    Name = guild.Name,
                    GuildId = guild.Id.ToString()
                };
                await _guildData.CreateGuild(newGuild);

                _logger.LogInformation($"TEST: Added {guild.Name} ({guild.Id}) to DB");

            }
        }
    }
}
