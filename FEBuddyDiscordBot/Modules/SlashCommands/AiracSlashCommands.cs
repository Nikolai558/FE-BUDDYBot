using Discord.Net;
using FEBuddyDiscordBot.Airac;
using FEBuddyDiscordBot.DataAccess;
using FEBuddyDiscordBot.Models;

namespace FEBuddyDiscordBot.Modules.SlashCommands;

public enum AiracPingSetting
{
    [ChoiceDisplay("On")]
    On,

    [ChoiceDisplay("Off")]
    Off,
}

/// <summary>
/// Lets members turn the "next cycle's MetaFile is out" pings off and on. Verified members get the ping role
/// automatically (see <see cref="Services.RoleAssignmentService"/>) unless they've turned the pings off here.
/// </summary>
public sealed class AiracSlashCommands : InteractionModuleBase<SocketInteractionContext>
{
    private readonly GuildSettingsStore _settings;
    private readonly AiracStore _store;
    private readonly ILogger<AiracSlashCommands> _logger;

    public AiracSlashCommands(GuildSettingsStore settings, AiracStore store, ILogger<AiracSlashCommands> logger)
    {
        _settings = settings;
        _store = store;
        _logger = logger;
    }

    [SlashCommand("airac-pings", "Turn the ping for each new AIRAC cycle's d-TPP MetaFile on or off.")]
    public async Task SetPingsAsync([Summary("turn", "On to get the pings, Off to stop them")] AiracPingSetting turn)
    {
        await RespondAsync(await SetAsync((SocketGuildUser)Context.User, turn == AiracPingSetting.On), ephemeral: true, allowedMentions: AllowedMentions.None);
    }

    [SlashCommand("airac", "Show the current and next AIRAC cycles, and whether the next cycle's d-TPP MetaFile is out.")]
    public async Task ShowCyclesAsync()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        AiracCycle current = AiracCycle.Current(DateOnly.FromDateTime(now.UtcDateTime));
        AiracCycle next = AiracCycle.Next(DateOnly.FromDateTime(now.UtcDateTime));
        DateTimeOffset nextStarts = new(next.EffectiveDate.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        GuildSettings settings = _settings.Current;

        string metafile;
        if (await _store.GetAnnouncedAtAsync(next.Id) is { } found)
        {
            metafile = $"✅ Out since <t:{found.ToUnixTimeSeconds()}:f>. You can run FE-Buddy for AIRAC **{next.Id}** and get your full export.";
        }
        else
        {
            // The FAA usually publishes it 15 to 18 days before the cycle starts.
            DateOnly from = next.EffectiveDate.AddDays(-18), to = next.EffectiveDate.AddDays(-15);
            metafile = $"Not out yet. The FAA usually publishes it {from:MMMM d} to {to:MMMM d}.";
            metafile += MetafileCheckTimes.NextAfter(now, settings.AiracCheckTimesUtc) is { } check
                ? $" The bot checks again <t:{check.ToUnixTimeSeconds()}:R>."
                : " The bot isn't checking for it right now.";
        }

        Embed embed = new EmbedBuilder()
            .WithTitle("AIRAC cycles")
            .WithColor(Color.Blue)
            .AddField("Current cycle", $"**{current.Id}**, in effect since {current.EffectiveDate:MMMM d, yyyy}")
            .AddField("Next cycle", $"**{next.Id}**, takes effect {next.EffectiveDate:MMMM d, yyyy} at 0000z (<t:{nextStarts.ToUnixTimeSeconds()}:R>)")
            .AddField($"AIRAC {next.Id} d-TPP MetaFile", metafile)
            .WithFooter(settings.AiracPingRoleId is null ? "" : "Get pinged when it's out: /airac-pings turn:On")
            .Build();

        await RespondAsync(embed: embed, ephemeral: true);
    }

    [ComponentInteraction(MetafilePing.StopButtonId, ignoreGroupNames: true)]
    public async Task StopPingsAsync()
    {
        await RespondAsync(await SetAsync((SocketGuildUser)Context.User, on: false), ephemeral: true, allowedMentions: AllowedMentions.None);
    }

    private async Task<string> SetAsync(SocketGuildUser user, bool on)
    {
        GuildSettings settings = _settings.Current;
        if (settings.AiracPingRoleId is not ulong roleId || user.Guild.GetRole(roleId) is not { } role)
        {
            return "AIRAC pings aren't set up on this server yet.";
        }

        if (on && settings.VerifiedRoleId is ulong verifiedId && user.Roles.All(r => r.Id != verifiedId))
        {
            return "You need the Verified role first. Run `/give-role`.";
        }

        await _store.SetOptedOutAsync(user.Id, optedOut: !on);
        _logger.LogInformation("AIRAC: {User} ({UserId}) turned pings {State}", user.Username, user.Id, on ? "on" : "off");

        bool has = user.Roles.Any(r => r.Id == role.Id);
        try
        {
            if (on && !has) await user.AddRoleAsync(role);
            else if (!on && has) await user.RemoveRoleAsync(role);
        }
        catch (HttpException ex) when (ex.DiscordCode == DiscordErrorCode.MissingPermissions)
        {
            _logger.LogWarning("Missing Permissions: could not {Action} {Role} for {User} ({UserId}). The bot's role must be above {Role}.",
                on ? "give" : "remove", role.Name, user.Username, user.Id, role.Name);
            return $"Saved, but I couldn't {(on ? "give you" : "remove")} {role.Mention}. Please let an admin know.";
        }

        return on
            ? $"You'll be pinged ({role.Mention}) when each new AIRAC cycle's MetaFile is out."
            : "You won't get AIRAC MetaFile pings any more. Turn them back on with `/airac-pings turn:On`.";
    }
}
