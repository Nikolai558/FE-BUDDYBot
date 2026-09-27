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

/// <summary>Which kinds of changes <see cref="RoleAssignmentService.ApplyAsync"/> may make.</summary>
[Flags]
public enum MemberChanges
{
    None = 0,
    AddRoles = 1,
    RemoveRoles = 2,
    Nickname = 4,
    All = AddRoles | RemoveRoles | Nickname,
}

/// <summary>
/// Read-only comparison of a member against VATUSA: what the bot would add, remove or rename. Nothing has been changed yet.
/// </summary>
public sealed record MemberCheck(
    SocketGuildUser User,
    VatusaLookupStatus Status,
    VatusaUser? Vatusa,
    IReadOnlyList<SocketRole> RolesToAdd,
    IReadOnlyList<SocketRole> RolesToRemove,
    IReadOnlyList<SocketRole> RolesKept,
    string? CurrentNickname,
    string? NewNickname,
    bool IsServerOwner,
    IReadOnlyList<string> Problems)
{
    public bool HasDifferences => RolesToAdd.Count > 0 || RolesToRemove.Count > 0 || NewNickname is not null;
}

/// <summary>
/// What happened when changes were applied to a member. <see cref="Problems"/> lists anything the bot could not do.
/// </summary>
public sealed record RoleAssignmentResult(
    RoleAssignmentOutcome Outcome,
    IReadOnlyList<IRole> Roles,
    IReadOnlyList<IRole> Added,
    IReadOnlyList<IRole> Removed,
    string? Nickname,
    bool NicknameChanged,
    IReadOnlyList<string> Problems)
{
    public static RoleAssignmentResult Empty(RoleAssignmentOutcome outcome) => new(outcome, [], [], [], null, false, []);

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
    /// Used by /give-role and the join/voice events: add any missing roles and update the nickname. Never removes roles.
    /// </summary>
    /// <param name="user">Member to update.</param>
    /// <param name="notifyIfNotLinked">Send the member a DM explaining how to link their account if it isn't linked.</param>
    public async Task<RoleAssignmentResult> AssignRolesAsync(SocketGuildUser user, bool notifyIfNotLinked)
    {
        MemberCheck check = await CheckAsync(user);

        if (check.Status == VatusaLookupStatus.Unavailable)
        {
            _logger.LogInformation("Roles: {User} ({UserId}) skipped; VATUSA unavailable", user.Username, user.Id);
            return RoleAssignmentResult.Empty(RoleAssignmentOutcome.VatusaUnavailable);
        }

        if (check.Status == VatusaLookupStatus.NotLinked)
        {
            _logger.LogInformation("Roles: {User} ({UserId}) is not linked on VATUSA", user.Username, user.Id);
            if (notifyIfNotLinked) await SendNotLinkedMessageAsync(user, _settings.Current);
            return RoleAssignmentResult.Empty(RoleAssignmentOutcome.NotLinked);
        }

        return await ApplyAsync(check, MemberChanges.AddRoles | MemberChanges.Nickname, logUnchanged: true);
    }

    /// <summary>
    /// Compare a member with VATUSA and work out which roles they should gain or lose and what their nickname should be.
    /// Makes no changes. Respects the /admin event switches (staff role, nicknames).
    /// </summary>
    public async Task<MemberCheck> CheckAsync(SocketGuildUser user)
    {
        GuildSettings settings = _settings.Current;
        VatusaLookup lookup = await _vatusa.GetUserByDiscordIdAsync(user.Id);
        bool isOwner = user.Id == user.Guild.OwnerId;

        if (lookup.Status == VatusaLookupStatus.Unavailable)
        {
            return new(user, lookup.Status, null, [], [], [], user.Nickname, null, isOwner, []);
        }

        (bool shouldBeVerified, bool shouldBeStaff) = Qualifications(lookup);

        List<SocketRole> toAdd = [], toRemove = [], kept = [];
        List<string> problems = [];

        void Compare(ulong? roleId, string label, bool shouldHave)
        {
            SocketRole? role = roleId is ulong id ? user.Guild.GetRole(id) : null;
            if (role is null)
            {
                if (shouldHave)
                {
                    WarnOnce($"role-missing-{label}", $"The {label} role is not set or no longer exists. Use /admin roles to choose it.");
                    problems.Add($"The {label} role isn't configured. Please let an admin know.");
                }
                return;
            }

            bool has = user.Roles.Any(r => r.Id == role.Id);
            if (shouldHave && !has) toAdd.Add(role);
            else if (!shouldHave && has) toRemove.Add(role);
            else if (has) kept.Add(role);
        }

        Compare(settings.VerifiedRoleId, "Verified", shouldBeVerified);
        if (settings.AssignStaffRole) Compare(settings.StaffRoleId, "Staff", shouldBeStaff);

        string? newNickname = null;
        if (lookup.User is not null && settings.ChangeNicknames && !isOwner)
        {
            string nickname = BuildNickname(user.Nickname, lookup.User);
            if (nickname != user.Nickname) newNickname = nickname;
        }

        return new(user, lookup.Status, lookup.User, toAdd, toRemove, kept, user.Nickname, newNickname, isOwner, problems);
    }

    /// <summary>
    /// Make the changes from a <see cref="CheckAsync"/> result, limited to the kinds of change allowed by <paramref name="changes"/>.
    /// </summary>
    /// <param name="logUnchanged">Log a summary line even if nothing changed (bulk checks only log members that changed).</param>
    public async Task<RoleAssignmentResult> ApplyAsync(MemberCheck check, MemberChanges changes, bool logUnchanged)
    {
        SocketGuildUser user = check.User;

        if (check.Status == VatusaLookupStatus.Unavailable)
        {
            return RoleAssignmentResult.Empty(RoleAssignmentOutcome.VatusaUnavailable);
        }

        List<string> problems = [.. check.Problems];
        List<IRole> added = [], removed = [];

        if (changes.HasFlag(MemberChanges.AddRoles))
        {
            foreach (SocketRole role in check.RolesToAdd)
            {
                if (await TryChangeRoleAsync(user, role, add: true, problems)) added.Add(role);
            }
        }

        if (changes.HasFlag(MemberChanges.RemoveRoles))
        {
            foreach (SocketRole role in check.RolesToRemove)
            {
                if (await TryChangeRoleAsync(user, role, add: false, problems)) removed.Add(role);
            }
        }

        string? nickname = check.NewNickname is null && !check.IsServerOwner ? check.CurrentNickname : null;
        bool nicknameChanged = false;
        if (changes.HasFlag(MemberChanges.Nickname) && check.NewNickname is not null)
        {
            try
            {
                await user.ModifyAsync(u => u.Nickname = check.NewNickname);
                nickname = check.NewNickname;
                nicknameChanged = true;
            }
            catch (HttpException ex) when (ex.DiscordCode == DiscordErrorCode.MissingPermissions)
            {
                _logger.LogWarning("Missing Permissions: could not change nickname for {User} ({UserId})", user.Username, user.Id);
                problems.Add("I couldn't change your nickname.");
            }
        }

        RoleAssignmentOutcome outcome = check.Status == VatusaLookupStatus.NotLinked ? RoleAssignmentOutcome.NotLinked : RoleAssignmentOutcome.Assigned;
        IReadOnlyList<IRole> rolesNow = [.. check.RolesKept, .. added, .. check.RolesToRemove.Except(removed)];
        RoleAssignmentResult result = new(outcome, rolesNow, added, removed, nickname, nicknameChanged, problems);

        LogSummary(check, result, logUnchanged);
        return result;
    }

    private async Task<bool> TryChangeRoleAsync(SocketGuildUser user, SocketRole role, bool add, List<string> problems)
    {
        try
        {
            if (add) await user.AddRoleAsync(role);
            else await user.RemoveRoleAsync(role);
            return true;
        }
        catch (HttpException ex) when (ex.DiscordCode == DiscordErrorCode.MissingPermissions)
        {
            _logger.LogWarning("Missing Permissions: could not {Action} {Role} for {User} ({UserId}). The bot's role must be above {Role}.",
                add ? "give" : "remove", role.Name, user.Username, user.Id, role.Name);
            problems.Add(add ? $"I couldn't give you {role.Mention}. An admin needs to move my role above it." : $"I couldn't remove {role.Mention}.");
            return false;
        }
    }

    // One line per member, e.g.:
    // Roles: nikolai558 (353684697651478528) VATUSA CID 1234567 ZLC | added: Verified | removed: none | kept: ARTCC STAFF | nickname: unchanged (server owner)
    private void LogSummary(MemberCheck check, RoleAssignmentResult result, bool logUnchanged)
    {
        bool changed = result.Added.Count > 0 || result.Removed.Count > 0 || result.NicknameChanged;
        if (!changed && !logUnchanged && result.Problems.Count == 0) return;

        static string Names(IEnumerable<IRole> roles) => roles.Any() ? string.Join(", ", roles.Select(r => r.Name)) : "none";

        string vatusa = check.Status == VatusaLookupStatus.NotLinked ? "not linked" : $"CID {check.Vatusa?.Cid} {check.Vatusa?.Facility}";
        string nickname = result.NicknameChanged ? $"'{check.CurrentNickname}' -> '{result.Nickname}'"
            : check.IsServerOwner ? "unchanged (server owner)"
            : check.NewNickname is not null ? $"not changed (would be '{check.NewNickname}')"
            : "unchanged";

        _logger.LogInformation("Roles: {User} ({UserId}) VATUSA {Vatusa} | added: {Added} | removed: {Removed} | kept: {Kept} | nickname: {Nickname}{Problems}",
            check.User.Username, check.User.Id, vatusa, Names(result.Added), Names(result.Removed), Names(check.RolesKept), nickname,
            result.Problems.Count > 0 ? " | problems: " + string.Join("; ", result.Problems) : "");
    }

    /// <summary>Which roles a member qualifies for, given their VATUSA lookup.</summary>
    internal static (bool Verified, bool Staff) Qualifications(VatusaLookup lookup)
    {
        bool linked = lookup.Status == VatusaLookupStatus.Found && lookup.User is not null;
        bool staff = linked && lookup.User!.Roles?.Any(r => r.Role is not null && StaffPositions.Contains(r.Role)) == true;
        return (linked, staff);
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
