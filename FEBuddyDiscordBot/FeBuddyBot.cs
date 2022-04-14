using FEBuddyDiscordBot.DataAccess;
using FEBuddyDiscordBot.Services;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http;
using Serilog;

namespace FEBuddyDiscordBot;
public class FeBuddyBot
{
    private static IConfigurationRoot _config;

    public async Task StartAsync()
    {
        var _builder = new ConfigurationBuilder().SetBasePath(AppContext.BaseDirectory).AddJsonFile(path: "config.json");
        _config = _builder.Build();

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

        await serviceProvider.GetRequiredService<StartupService>().StartAsync();

        serviceProvider.GetRequiredService<CommandHandler>();

        serviceProvider.GetRequiredService<RoleAssignmentService>();

        await Task.Delay(-1);
    }

    private void ConfigureServices(IServiceCollection services)
    {
        services.AddLogging(configure => configure.AddSerilog());

        services.RemoveAll<IHttpMessageHandlerBuilderFilter>();

        var logLevel = _config["logLevel"];
        var level = Serilog.Events.LogEventLevel.Error;

        if (!string.IsNullOrEmpty(logLevel))
        {
            switch (logLevel.ToLower())
            {
                case "error": { level = Serilog.Events.LogEventLevel.Error; break; }
                case "info": { level = Serilog.Events.LogEventLevel.Information; break; }
                case "debug": { level = Serilog.Events.LogEventLevel.Debug; break; }
                case "crit": { level = Serilog.Events.LogEventLevel.Fatal; break; }
                case "warn": { level = Serilog.Events.LogEventLevel.Warning; break; }
                case "trace": { level = Serilog.Events.LogEventLevel.Debug; break; }
            }
        }

        Log.Logger = new LoggerConfiguration()
            .WriteTo.File("logs/fe-buddy-bot.log", rollingInterval: RollingInterval.Day)
            .WriteTo.Console()
            .MinimumLevel.Is(level)
            .CreateLogger();
    }
}
