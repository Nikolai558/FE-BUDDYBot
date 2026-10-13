using System.Diagnostics;
using System.Reflection;
using FEBuddyDiscordBot.Airac;
using FEBuddyDiscordBot.DataAccess;
using FEBuddyDiscordBot.Services;

namespace FEBuddyDiscordBot.Modules.SlashCommands;

/// <summary>
/// Commands only the bot's owner (the Discord application owner) can run.
/// </summary>
[Group("owner", "Bot owner commands")]
[DefaultMemberPermissions(GuildPermission.Administrator)]
[RequireOwner]
public sealed class OwnerSlashCommands : InteractionModuleBase<SocketInteractionContext>
{
    private readonly BotHealth _health;
    private readonly GitHubApi _github;
    private readonly VatusaApi _vatusa;
    private readonly GuildSettingsStore _settings;

    public OwnerSlashCommands(BotHealth health, GitHubApi github, VatusaApi vatusa, GuildSettingsStore settings)
    {
        _health = health;
        _github = github;
        _vatusa = vatusa;
        _settings = settings;
    }

    [SlashCommand("set-status", "Set the status text shown under the bot's name (until the bot restarts).")]
    public async Task SetStatusAsync([Summary("text", "Status text. Leave empty to clear it.")] string? text = null)
    {
        await Context.Client.SetCustomStatusAsync(text ?? "");
        await RespondAsync(string.IsNullOrWhiteSpace(text) ? "Status cleared." : $"Status set to: {text}", ephemeral: true);
    }

    [SlashCommand("status", "Show the bot's version, uptime, and how its background jobs and connections are doing.")]
    public async Task ShowStatusAsync()
    {
        await DeferAsync(ephemeral: true);

        // Live checks, side by side. A VATUSA lookup of the owner's own account shows whether VATUSA answers.
        Task<VatusaLookup> vatusa = _vatusa.GetUserByDiscordIdAsync(Context.User.Id);
        Task<GitHubRateLimit>? github = _github.IsConfigured ? _github.GetRateLimitAsync() : null;

        string vatusaLine = (await vatusa).Status == VatusaLookupStatus.Unavailable ? "⚠️ not answering" : "✅ answering";
        string githubLine;
        if (github is null)
        {
            githubLine = "GitHub App not configured";
        }
        else
        {
            try
            {
                GitHubRateLimit limit = await github;
                githubLine = $"✅ {_github.Repository}, {limit.Remaining:N0} of {limit.Limit:N0} requests left (resets <t:{limit.ResetsAt.ToUnixTimeSeconds()}:R>)";
            }
            catch (Exception ex)
            {
                githubLine = $"⚠️ {Short(ex.Message)}";
            }
        }

        using Process process = Process.GetCurrentProcess();
        string version = StartupService.Version(typeof(StartupService).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion);
        DateTimeOffset? nextAirac = MetafileCheckTimes.NextAfter(DateTimeOffset.UtcNow, _settings.Current.AiracCheckTimesUtc);

        Embed embed = new EmbedBuilder()
            .WithTitle("FE-Buddy bot status")
            .WithColor(Color.Blue)
            .AddField("Bot",
                $"Version: `{version}`\n" +
                $"Started: <t:{_health.StartedUtc.ToUnixTimeSeconds()}:R> (<t:{_health.StartedUtc.ToUnixTimeSeconds()}:f>)\n" +
                $"Memory: {process.WorkingSet64 / (1024 * 1024):N0} MB")
            .AddField("Connections",
                $"Discord: {(Context.Client.ConnectionState == ConnectionState.Connected ? "✅" : "⚠️")} {Context.Client.ConnectionState}, {Context.Client.Latency} ms\n" +
                $"VATUSA: {vatusaLine}\n" +
                $"GitHub: {githubLine}")
            .AddField("Background jobs (since the last restart)",
                $"AIRAC MetaFile check: {Run(_health.AiracCheck)}" + (nextAirac is { } next ? $"\n-# Next check <t:{next.ToUnixTimeSeconds()}:R>" : "\n-# Checks are off") + "\n" +
                $"Issue sync: {(_github.IsConfigured ? Run(_health.IssueSync) : "off")}\n" +
                $"Release check: {(_github.IsConfigured ? Run(_health.ReleaseCheck) : "off")}")
            .Build();

        await FollowupAsync(embed: embed, ephemeral: true);
    }

    private static string Run(JobRun? run) => run is null
        ? "not run yet"
        : $"{(run.Ok ? "✅" : "⚠️")} <t:{run.AtUtc.ToUnixTimeSeconds()}:R>: {Short(run.Detail)}";

    // Embed fields hold 1,024 characters; error messages can be long.
    private static string Short(string text) => text.Length <= 200 ? text : text[..199] + "…";
}
