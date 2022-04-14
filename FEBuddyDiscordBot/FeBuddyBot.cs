using FEBuddyDiscordBot.DataAccess;
using FEBuddyDiscordBot.Services;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http;
using Serilog;
using Serilog.Events;

namespace FEBuddyDiscordBot;
public class FeBuddyBot
{
    private static IConfigurationRoot _config;

    public async Task StartAsync()
    {
        var _builder = new ConfigurationBuilder().SetBasePath(AppContext.BaseDirectory).AddJsonFile(path: "config.json");
        _config = _builder.Build();

        VerifyConfigItems();

        DiscordSocketConfig discordConfig = new DiscordSocketConfig()
        {
            GatewayIntents = GatewayIntents.GuildMembers | GatewayIntents.DirectMessages | GatewayIntents.GuildMessages | GatewayIntents.Guilds | GatewayIntents.GuildVoiceStates
        };

        var services = new ServiceCollection()
            .AddSingleton(new DiscordSocketClient(discordConfig))
            .AddSingleton(_config)
            .AddSingleton(new CommandService(new CommandServiceConfig { DefaultRunMode = RunMode.Async, LogLevel = LogSeverity.Debug, CaseSensitiveCommands = false, ThrowOnError = false }))
            .AddSingleton<StartupService>()
            .AddSingleton<LoggingService>()
            .AddSingleton<CommandHandler>()
            .AddSingleton<RoleAssignmentService>()
            .AddSingleton<VatusaApi>();

        ConfigureServices(services);

        var serviceProvider = services.BuildServiceProvider();

        serviceProvider.GetRequiredService<LoggingService>();

        await serviceProvider.GetRequiredService<StartupService>().StartAsync(UseDevToken: true);

        serviceProvider.GetRequiredService<CommandHandler>();

        serviceProvider.GetRequiredService<RoleAssignmentService>();

        await Task.Delay(-1);
    }

    private void ConfigureServices(IServiceCollection services)
    {
        services.AddLogging(configure => configure.AddSerilog());

        services.RemoveAll<IHttpMessageHandlerBuilderFilter>();

        string logLevel = _config["logLevel"];
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

    private void VerifyConfigItems()
    {
        string[] RequiredConfigItems = new List<string>() 
        {
            "token",
            "prefix",
            "logLevel"
        }.ToArray();

        foreach (string requiredConfigKey in RequiredConfigItems)
        {
            if (string.IsNullOrEmpty(_config[requiredConfigKey]) || string.IsNullOrWhiteSpace(_config[requiredConfigKey]))
            {
                throw new Exception($"Required Config Item '{requiredConfigKey}' is missing.");
            }
        }
    }
}
