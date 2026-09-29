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
builder.Services.Configure<GitHubOptions>(builder.Configuration.GetSection(GitHubOptions.SectionName));

// Discord
builder.Services.AddSingleton(new DiscordSocketConfig
{
    // GuildMembers is a privileged intent: it must also be enabled in the Discord Developer Portal.
    // GuildMessages: edits and deletes of replies in issue posts. MessageContent (privileged, opt-in with
    // Bot:MessageContentIntent) is needed to copy every reply's text to GitHub.
    GatewayIntents = GatewayIntents.Guilds | GatewayIntents.GuildMembers | GatewayIntents.GuildVoiceStates | GatewayIntents.GuildMessages
        | (builder.Configuration.GetValue<bool>($"{BotOptions.SectionName}:{nameof(BotOptions.MessageContentIntent)}") ? GatewayIntents.MessageContent : GatewayIntents.None),
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
builder.Services.AddHttpClient(GitHubApi.HttpClientName, http =>
{
    http.BaseAddress = new Uri("https://api.github.com/");
    http.Timeout = TimeSpan.FromSeconds(20);
    http.DefaultRequestHeaders.UserAgent.ParseAdd("FE-BUDDYBot (+https://github.com/Nikolai558/FE-BUDDYBot)");
    http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    http.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
});
builder.Services.AddHttpClient(IssueSubmissionService.FilesHttpClientName, http => http.Timeout = TimeSpan.FromSeconds(30));
builder.Services.AddHttpClient(GitHubDeviceFlow.HttpClientName, http =>
{
    http.BaseAddress = new Uri("https://github.com/");
    http.Timeout = TimeSpan.FromSeconds(20);
    http.DefaultRequestHeaders.UserAgent.ParseAdd("FE-BUDDYBot (+https://github.com/Nikolai558/FE-BUDDYBot)");
});
builder.Services.AddHttpClient(IssueForumService.DiscordApiHttpClientName, http =>
{
    http.BaseAddress = new Uri("https://discord.com/api/v10/");
    http.Timeout = TimeSpan.FromSeconds(15);
    http.DefaultRequestHeaders.UserAgent.ParseAdd("DiscordBot (https://github.com/Nikolai558/FE-BUDDYBot, 1.0)");
});

// App services
builder.Services.AddSingleton<BotDatabase>();
builder.Services.AddSingleton<GuildSettingsStore>();
builder.Services.AddSingleton<VatusaApi>();
builder.Services.AddSingleton<LoggingService>();
builder.Services.AddSingleton<InteractionHandler>();
builder.Services.AddSingleton<RoleAssignmentService>();

// GitHub issues
builder.Services.AddSingleton<IssueStore>();
builder.Services.AddSingleton<GitHubAppAuth>();
builder.Services.AddSingleton<GitHubApi>();
builder.Services.AddSingleton<GitHubDeviceFlow>();
builder.Services.AddSingleton<IssueForumService>();
builder.Services.AddSingleton<IssueSubmissionService>();
builder.Services.AddSingleton<IssueInteractionHandler>();
builder.Services.AddSingleton<IssueReplyService>();

// Background services, started in this order
builder.Services.AddHostedService<StartupService>();
builder.Services.AddHostedService<ConnectionWatchdog>();
builder.Services.AddHostedService<StatusUpdateService>();
builder.Services.AddHostedService<IssueSyncService>();

builder.Build().Run();
