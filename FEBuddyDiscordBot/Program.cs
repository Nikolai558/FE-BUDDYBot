using FEBuddyDiscordBot.DataAccess;
using FEBuddyDiscordBot.Models;
using FEBuddyDiscordBot.Services;
using Serilog;

// Configuration comes from appsettings.json, appsettings.{Environment}.json, user-secrets (Development only),
// and environment variables (e.g. Bot__Token), in that order.
HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

builder.Services.AddSerilog((services, logger) => logger
    .ReadFrom.Configuration(builder.Configuration)
    .ReadFrom.Services(services));

builder.Services.AddOptions<BotOptions>()
    .Bind(builder.Configuration.GetSection(BotOptions.SectionName))
    .Validate(o => !string.IsNullOrWhiteSpace(o.Token), "Bot:Token is missing. Set it with user-secrets or the Bot__Token environment variable.")
    .Validate(o => o.GuildId != 0, "Bot:GuildId is missing.")
    .ValidateOnStart();
builder.Services.Configure<HeartbeatOptions>(builder.Configuration.GetSection(HeartbeatOptions.SectionName));
builder.Services.Configure<GuildDefaultsOptions>(builder.Configuration.GetSection(GuildDefaultsOptions.SectionName));

// Discord
builder.Services.AddSingleton(new DiscordSocketConfig
{
    // GuildMembers is a privileged intent: it must also be enabled in the Discord Developer Portal.
    GatewayIntents = GatewayIntents.Guilds | GatewayIntents.GuildMembers | GatewayIntents.GuildVoiceStates,
    AlwaysDownloadUsers = true,
    LogLevel = LogSeverity.Info,
});
builder.Services.AddSingleton<DiscordSocketClient>();
builder.Services.AddSingleton(services => new InteractionService(
    services.GetRequiredService<DiscordSocketClient>(),
    new InteractionServiceConfig { LogLevel = LogSeverity.Info, DefaultRunMode = RunMode.Async }));

// HTTP
builder.Services.AddHttpClient(VatusaApi.HttpClientName, http =>
{
    http.BaseAddress = new Uri("https://api.vatusa.net/");
    http.Timeout = TimeSpan.FromSeconds(15);
    http.DefaultRequestHeaders.UserAgent.ParseAdd("FE-BUDDYBot (+https://github.com/Nikolai558/FE-BUDDYBot)");
});
builder.Services.AddHttpClient(StatusUpdateService.HttpClientName, http => http.Timeout = TimeSpan.FromSeconds(10));

// App services
builder.Services.AddSingleton<GuildSettingsStore>();
builder.Services.AddSingleton<VatusaApi>();
builder.Services.AddSingleton<LoggingService>();
builder.Services.AddSingleton<InteractionHandler>();
builder.Services.AddSingleton<RoleAssignmentService>();

// Background services, started in this order
builder.Services.AddHostedService<StartupService>();
builder.Services.AddHostedService<ConnectionWatchdog>();
builder.Services.AddHostedService<StatusUpdateService>();

builder.Build().Run();
