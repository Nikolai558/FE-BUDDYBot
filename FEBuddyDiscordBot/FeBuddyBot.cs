using Discord.Interactions;
using FEBuddyDiscordBot.DataAccess;
using FEBuddyDiscordBot.DataAccess.DB;
using FEBuddyDiscordBot.Services;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http;
using Serilog;
using Serilog.Events;

namespace FEBuddyDiscordBot;
public class FeBuddyBot
{
    #pragma warning disable CS8618
    private static IConfiguration _config;

    public async Task StartAsync()
    {
        var _builder = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile(path: "appsettings.json")
            .AddUserSecrets<Program>();

        _config = _builder.Build();

        DiscordSocketConfig discordConfig = new()
        {
            GatewayIntents = GatewayIntents.GuildMembers | GatewayIntents.DirectMessages | GatewayIntents.GuildMessages | GatewayIntents.Guilds | GatewayIntents.GuildVoiceStates
        };

        DiscordSocketClient discordClient = new(discordConfig);

        var services = new ServiceCollection()
            .AddSingleton(discordClient)
            .AddSingleton(_config)
            .AddSingleton(new CommandService(new CommandServiceConfig { DefaultRunMode = Discord.Commands.RunMode.Async, LogLevel = LogSeverity.Debug, CaseSensitiveCommands = false, ThrowOnError = false }))
            .AddSingleton<StartupService>()
            .AddSingleton<LoggingService>()
            .AddSingleton<CommandHandler>()
            .AddSingleton(new InteractionService(discordClient, new InteractionServiceConfig { LogLevel = LogSeverity.Debug }))
            .AddSingleton<InteractionHandler>()
            .AddSingleton<IMongoDbConnection, MongoDbConnection>()
            .AddSingleton<IMongoGuildData, MongoGuildData>()
            .AddSingleton<DataBaseService>()
            .AddSingleton<RoleAssignmentService>()
            .AddSingleton<VatusaApi>();

        ConfigureServices(services);

        var serviceProvider = services.BuildServiceProvider();

        serviceProvider.GetRequiredService<LoggingService>();

        serviceProvider.GetRequiredService<InteractionHandler>();

        serviceProvider.GetRequiredService<CommandHandler>();

        serviceProvider.GetRequiredService<RoleAssignmentService>();

        await serviceProvider.GetRequiredService<StartupService>().StartAsync(UseDevToken: true);

        await Task.Delay(-1);
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddLogging(configure => configure.AddSerilog());

        services.RemoveAll<IHttpMessageHandlerBuilderFilter>();

        string logLevel = _config.GetRequiredSection("Logging").GetRequiredSection("LogLevel").Value;
        LogEventLevel level = LogEventLevel.Error;

        if (!string.IsNullOrEmpty(logLevel))
        {
            switch (logLevel.ToLower())
            {
                case "error": { level = LogEventLevel.Error; break; }
                case "info": { level = LogEventLevel.Information; break; }
                case "debug": { level = LogEventLevel.Debug; break; }
                case "crit": { level = LogEventLevel.Fatal; break; }
                case "warn": { level = LogEventLevel.Warning; break; }
                case "trace": { level = LogEventLevel.Debug; break; }
            }
        }

        Log.Logger = new LoggerConfiguration()
            .WriteTo.File("logs/fe-buddy-bot.log", rollingInterval: RollingInterval.Day)
            .WriteTo.Console()
            .MinimumLevel.Is(level)
            .CreateLogger();
    }
}
