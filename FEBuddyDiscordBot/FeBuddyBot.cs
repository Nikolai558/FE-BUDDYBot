using Discord.Interactions;
using FEBuddyDiscordBot.DataAccess;
using FEBuddyDiscordBot.DataAccess.DB;
using FEBuddyDiscordBot.Services;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http;
using Serilog;
using Serilog.Events;

namespace FEBuddyDiscordBot;

/// <summary>
/// Main class for the FE Buddy Discord Bot
/// </summary>
public class FeBuddyBot
{
    #pragma warning disable CS8618 // Disable annoying Visual Studio Warning
    private static IConfiguration _config; // Private variable for our configuration settings for the bot.

    /// <summary>
    /// Create and start loading everything needed for our Discord Bot
    /// </summary>
    /// <returns>None</returns>
    public async Task StartAsync()
    {
        // Load our configurations, Insert any User Secrets and build the Configuration.
        var _builder = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile(path: "appsettings.json")
            .AddUserSecrets<Program>();
        _config = _builder.Build();

        // Specify the Discord Socket Client configurations
        DiscordSocketConfig discordConfig = new()
        {
            GatewayIntents = GatewayIntents.GuildMembers | GatewayIntents.DirectMessages | GatewayIntents.GuildMessages | GatewayIntents.Guilds | GatewayIntents.GuildVoiceStates
        };

        // Create our Discord Client using the configurations above.
        DiscordSocketClient discordClient = new(discordConfig);

        // Dependency injection, add all of our services/modules needed to run the discord bot.
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

        // Modify our servies
        ConfigureServices(services);

        // Build the Dependency Injection service provider.
        var serviceProvider = services.BuildServiceProvider();

        // Initialize required services/modules
        serviceProvider.GetRequiredService<LoggingService>();
        serviceProvider.GetRequiredService<InteractionHandler>();
        serviceProvider.GetRequiredService<CommandHandler>();
        serviceProvider.GetRequiredService<RoleAssignmentService>();
        await serviceProvider.GetRequiredService<InteractionHandler>().InitializeAsync();

        // Start the bot log in process.
        await serviceProvider.GetRequiredService<StartupService>().StartAsync(UseDevToken: true);

        // Keep the application running until bot shuts down or crashes
        await Task.Delay(-1);
    }

    /// <summary>
    /// Configure Servies and set Log level and output.
    /// </summary>
    /// <param name="services">Dependency Injection Service Collection.</param>
    private static void ConfigureServices(IServiceCollection services)
    {
        // Configure Serilog
        services.AddLogging(configure => configure.AddSerilog());

        // Remove HttpMessageHandler stuff, as it is very very verbose.
        services.RemoveAll<IHttpMessageHandlerBuilderFilter>();

        // Load the log file from our configuration and set it.
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

        // Tell the logger where to write logs to; write to a file and to the console. 
        Log.Logger = new LoggerConfiguration()
            .WriteTo.File("logs/fe-buddy-bot.log", rollingInterval: RollingInterval.Day)
            .WriteTo.Console()
            .MinimumLevel.Is(level)
            .CreateLogger();
    }
}
