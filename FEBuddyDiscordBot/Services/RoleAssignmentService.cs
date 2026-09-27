using System.Collections.Concurrent;
using Discord.Net;
using FEBuddyDiscordBot.DataAccess;
using FEBuddyDiscordBot.Models;

namespace FEBuddyDiscordBot.Services;

public enum RoleAssignmentOutcome
{
    Assigned,
    NotLinked,
    VatusaUnavailable,
}

/// <summary>
/// What happened when roles were assigned to a member. <see cref="Problems"/> lists anything the bot could not do.
/// </summary>
public sealed record RoleAssignmentResult(
    RoleAssignmentOutcome Outcome,
    IReadOnlyList<IRole> Roles,
    string? Nickname,
    IReadOnlyList<string> Problems)
{
    public Embed ToEmbed()
    {
        EmbedBuilder embed = Outcome switch
        {
            RoleAssignmentOutcome.NotLinked => new EmbedBuilder()
                .WithColor(Color.Red)
                .WithTitle("Not Linked")
                .WithDescription("Your Discord account is not linked on VATUSA. Link it here, then try again:\nhttps://vatusa.net/my/profile"),
            RoleAssignmentOutcome.VatusaUnavailable => new EmbedBuilder()
                .WithColor(Color.Orange)
                .WithTitle("VATUSA is unavailable")
                .WithDescription("I couldn't reach VATUSA just now. Please try again in a few minutes."),
            _ => new EmbedBuilder()
                .WithColor(Problems.Count == 0 ? Color.Green : Color.Orange)
                .WithTitle("Your roles have been assigned")
                .WithDescription(Roles.Count == 0 ? "No roles to assign." : string.Join(" ", Roles.Select(r => r.Mention))),
        };

        if (Nickname is not null) embed.WithFooter($"Your nickname is: {Nickname}");
        if (Problems.Count > 0) embed.AddField("Heads up", string.Join("\n", Problems.Select(p => $"• {p}")));

        return embed.Build();
    }
}

/// <summary>
/// Assigns roles and nicknames to members based on their VATUSA account, and manages the private meeting role.
/// </summary>
public sealed class RoleAssignmentService
{
    // VATUSA facility staff positions that earn the staff role.
    private static readonly HashSet<string> StaffPositions = new(StringComparer.OrdinalIgnoreCase) { "ATM", "DATM", "TA", "EC", "FE", "WM" };

    private const int MaxNicknameLength = 32;

    private readonly VatusaApi _vatusa;
    private readonly GuildSettingsStore _settings;
    private readonly ILogger<RoleAssignmentService> _logger;
    private readonly ulong _guildId;

    // Configuration problems are logged once per run instead of on every event.
    private readonly ConcurrentDictionary<string, byte> _warned = new();

    public RoleAssignmentService(
        DiscordSocketClient discord,
        VatusaApi vatusa,
        GuildSettingsStore settings,
        IOptions<BotOptions> options,
        ILogger<RoleAssignmentService> logger)
    {
        _vatusa = vatusa;
        _settings = settings;
        _logger = logger;
        _guildId = options.Value.GuildId;

        discord.UserJoined += OnUserJoined;
        discord.UserVoiceStateUpdated += OnUserVoiceStateUpdated;
    }

    private Task OnUserJoined(SocketGuildUser user)
    {
        if (user.Guild.Id != _guildId || user.IsBot) return Task.CompletedTask;

        _logger.LogInformation("User Joined: {User} ({UserId})", user.Username, user.Id);

        if (_settings.Current.AssignRolesOnJoin)
        {
            RunInBackground("assign roles on join", () => AssignRolesAsync(user, notifyIfNotLinked: true));
        }

        return Task.CompletedTask;
    }

    private Task OnUserVoiceStateUpdated(SocketUser socketUser, SocketVoiceState before, SocketVoiceState after)
    {
        if (socketUser is not SocketGuildUser user || user.Guild.Id != _guildId || user.IsBot) return Task.CompletedTask;

        // Ignore mute/deafen/stream/video changes: only act when the user actually changes channel.
        if (before.VoiceChannel?.Id == after.VoiceChannel?.Id) return Task.CompletedTask;

        GuildSettings settings = _settings.Current;

        if (settings.PrivateMeetingRoleEnabled)
        {
            RunInBackground("update private meeting role", () => UpdatePrivateMeetingRoleAsync(user, before, after, settings));
        }

        // Only when connecting to voice (not when hopping between channels) to avoid repeated VATUSA lookups.
        if (settings.AssignRolesOnVoiceJoin && before.VoiceChannel is null && after.VoiceChannel is not null)
        {
            RunInBackground("assign roles on voice join", () => AssignRolesAsync(user, notifyIfNotLinked: false));
        }

        return Task.CompletedTask;
    }

    private async Task UpdatePrivateMeetingRoleAsync(SocketGuildUser user, SocketVoiceState before, SocketVoiceState after, GuildSettings settings)
    {
        if (settings.PrivateMeetingChannelId is not ulong channelId || settings.PrivateMeetingRoleId is not ulong roleId)
        {
            WarnOnce("private-meeting-unset", "Private meeting role is enabled but its role or voice channel is not set. Use /admin roles and /admin channels.");
            return;
        }

        SocketRole? role = user.Guild.GetRole(roleId);
        if (role is null)
        {
            WarnOnce("private-meeting-role-missing", $"Private meeting role {roleId} no longer exists. Use /admin roles to choose it again.");
            return;
        }

        bool wasInMeeting = before.VoiceChannel?.Id == channelId;
        bool isInMeeting = after.VoiceChannel?.Id == channelId;

        if (wasInMeeting && !isInMeeting)
        {
            await user.RemoveRoleAsync(role);
            _logger.LogInformation("Remove Role: {User} ({UserId}) left the private meeting; removed {Role}", user.Username, user.Id, role.Name);
        }
        else if (!wasInMeeting && isInMeeting)
        {
            await user.AddRoleAsync(role);
            _logger.LogInformation("Give Role: {User} ({UserId}) joined the private meeting; added {Role}", user.Username, user.Id, role.Name);
        }
    }

    /// <summary>
    /// Look the member up on VATUSA and give them the verified role, the staff role (if applicable), and their nickname.
    /// </summary>
    /// <param name="user">Member to update.</param>
    /// <param name="notifyIfNotLinked">Send the member a DM explaining how to link their account if it isn't linked.</param>
    public async Task<RoleAssignmentResult> AssignRolesAsync(SocketGuildUser user, bool notifyIfNotLinked)
    {
        GuildSettings settings = _settings.Current;
        VatusaLookup lookup = await _vatusa.GetUserByDiscordIdAsync(user.Id);

        if (lookup.Status == VatusaLookupStatus.Unavailable)
        {
            return new(RoleAssignmentOutcome.VatusaUnavailable, [], null, []);
        }

        if (lookup.Status == VatusaLookupStatus.NotLinked || lookup.User is null)
        {
            _logger.LogInformation("No Role: {User} ({UserId}) is not linked on VATUSA", user.Username, user.Id);
            if (notifyIfNotLinked) await SendNotLinkedMessageAsync(user, settings);
            return new(RoleAssignmentOutcome.NotLinked, [], null, []);
        }

        VatusaUser vatusaUser = lookup.User;
        List<IRole> roles = [];
        List<string> problems = [];

        await GiveRoleAsync(user, settings.VerifiedRoleId, "Verified", roles, problems);

        if (settings.AssignStaffRole && IsFacilityStaff(vatusaUser))
        {
            await GiveRoleAsync(user, settings.StaffRoleId, "Staff", roles, problems);
        }

        string? nickname = null;
        if (settings.ChangeNicknames)
        {
            nickname = await ChangeNicknameAsync(user, vatusaUser, problems);
        }

        return new(RoleAssignmentOutcome.Assigned, roles, nickname, problems);
    }

    private async Task GiveRoleAsync(SocketGuildUser user, ulong? roleId, string label, List<IRole> assigned, List<string> problems)
    {
        SocketRole? role = roleId is ulong id ? user.Guild.GetRole(id) : null;
        if (role is null)
        {
            WarnOnce($"role-missing-{label}", $"The {label} role is not set or no longer exists. Use /admin roles to choose it.");
            problems.Add($"The {label} role isn't configured. Please let an admin know.");
            return;
        }

        if (user.Roles.Any(r => r.Id == role.Id))
        {
            assigned.Add(role);
            return;
        }

        try
        {
            await user.AddRoleAsync(role);
            assigned.Add(role);
            _logger.LogInformation("Give Role: {User} ({UserId}) -> {Role}", user.Username, user.Id, role.Name);
        }
        catch (HttpException ex) when (ex.DiscordCode == DiscordErrorCode.MissingPermissions)
        {
            _logger.LogWarning("Missing Permissions: could not give {Role} to {User} ({UserId}). The bot's role must be above {Role}.", role.Name, user.Username, user.Id, role.Name);
            problems.Add($"I couldn't give you {role.Mention}. An admin needs to move my role above it.");
        }
    }

    private async Task<string?> ChangeNicknameAsync(SocketGuildUser user, VatusaUser vatusaUser, List<string> problems)
    {
        string nickname = BuildNickname(user.Nickname, vatusaUser);

        if (nickname == user.Nickname) return nickname;

        if (user.Id == user.Guild.OwnerId)
        {
            // Discord never lets bots rename the server owner.
            _logger.LogDebug("Nickname: skipping server owner {User}", user.Username);
            return null;
        }

        string? oldNickname = user.Nickname;
        try
        {
            await user.ModifyAsync(u => u.Nickname = nickname);
            _logger.LogInformation("Nickname: {User} ({UserId}) from '{Old}' to '{New}'", user.Username, user.Id, oldNickname, nickname);
            return nickname;
        }
        catch (HttpException ex) when (ex.DiscordCode == DiscordErrorCode.MissingPermissions)
        {
            _logger.LogWarning("Missing Permissions: could not change nickname for {User} ({UserId})", user.Username, user.Id);
            problems.Add("I couldn't change your nickname.");
            return null;
        }
    }

    /// <summary>
    /// Build "First Last | FAC". If the member already has a nickname with a "|", keep what is before it.
    /// Members with VATUSA name privacy get their CID instead of their name.
    /// </summary>
    internal static string BuildNickname(string? currentNickname, VatusaUser vatusaUser)
    {
        string suffix = $" | {vatusaUser.Facility}";

        string name;
        int pipe = currentNickname?.IndexOf('|') ?? -1;
        if (pipe >= 0)
        {
            name = currentNickname![..pipe].TrimEnd();
        }
        else if (vatusaUser.NamePrivacy == true)
        {
            name = vatusaUser.Cid?.ToString() ?? "";
        }
        else
        {
            name = $"{vatusaUser.FirstName} {vatusaUser.LastName}".Trim();
        }

        // Discord nicknames are limited to 32 characters; shorten the name part if needed.
        int maxNameLength = MaxNicknameLength - suffix.Length;
        if (name.Length > maxNameLength) name = name[..maxNameLength].TrimEnd();

        return name + suffix;
    }

    private static bool IsFacilityStaff(VatusaUser vatusaUser) =>
        vatusaUser.Roles?.Any(r => r.Role is not null && StaffPositions.Contains(r.Role)) == true;

    private async Task SendNotLinkedMessageAsync(SocketGuildUser user, GuildSettings settings)
    {
        string where = settings.RolesChannelId is ulong channelId ? $"in <#{channelId}>" : $"in the `{user.Guild.Name}` server";

        string message =
            $"Hello, I'm the bot that sets up your `{user.Guild.Name}` Discord roles.\n\n" +
            "To do that, I need your Discord account linked on VATUSA. Go to your VATUSA profile https://vatusa.net/my/profile and use \"VATUSA Discord Link\".\n\n" +
            $"When you're done, join a voice channel or run `/give-role` {where}.\n\n" +
            "If you can't do this, please message one of the server's administrators.";

        try
        {
            await user.SendMessageAsync(message);
        }
        catch (HttpException ex) when (ex.DiscordCode == DiscordErrorCode.CannotSendMessageToUser)
        {
            _logger.LogInformation("DM: {User} ({UserId}) does not accept direct messages", user.Username, user.Id);
        }
    }

    private void WarnOnce(string key, string message)
    {
        if (_warned.TryAdd(key, 0)) _logger.LogWarning("Config: {Message}", message);
    }

    // Discord event handlers must return quickly or they block the gateway, so real work runs in the background.
    private void RunInBackground(string description, Func<Task> work)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await work();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to {Description}", description);
            }
        });
    }
}
