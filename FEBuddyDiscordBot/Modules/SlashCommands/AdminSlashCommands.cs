using FEBuddyDiscordBot.DataAccess;
using FEBuddyDiscordBot.Models;
using FEBuddyDiscordBot.Services;

namespace FEBuddyDiscordBot.Modules.SlashCommands;

/// <summary>
/// Bot configuration for members with Manage Server (or the bot owner).
/// Every option is optional: only the options you fill in are changed.
/// </summary>
[Group("admin", "Bot configuration (requires Manage Server)")]
[DefaultMemberPermissions(GuildPermission.ManageGuild)]
[RequireUserPermission(GuildPermission.ManageGuild, Group = "AdminPermission")]
[RequireOwner(Group = "AdminPermission")]
public sealed class AdminSlashCommands : InteractionModuleBase<SocketInteractionContext>
{
    private readonly GuildSettingsStore _settings;
    private readonly GitHubApi _github;
    private readonly IssueForumService _issueForum;
    private readonly BotOptions _options;
    private readonly ILogger<AdminSlashCommands> _logger;

    public AdminSlashCommands(GuildSettingsStore settings, GitHubApi github, IssueForumService issueForum, IOptions<BotOptions> options, ILogger<AdminSlashCommands> logger)
    {
        _settings = settings;
        _github = github;
        _issueForum = issueForum;
        _options = options.Value;
        _logger = logger;
    }

    [SlashCommand("settings", "Show the bot's current configuration.")]
    public async Task ShowSettingsAsync()
    {
        await RespondAsync(embed: BuildSettingsEmbed(_settings.Current), ephemeral: true);
    }

    [SlashCommand("events", "Turn the bot's automatic behaviors on or off.")]
    public async Task SetEventsAsync(
        [Summary("assign-on-join", "Assign roles when someone joins the server")] bool? assignOnJoin = null,
        [Summary("assign-on-voice-join", "Assign roles when someone connects to voice")] bool? assignOnVoiceJoin = null,
        [Summary("private-meeting-role", "Give the private meeting role while someone is in the private meeting channel")] bool? privateMeetingRole = null,
        [Summary("change-nicknames", "Set nicknames to \"First Last | ARTCC\"")] bool? changeNicknames = null,
        [Summary("assign-staff-role", "Give the staff role to ARTCC staff (ATM, DATM, TA, EC, FE, WM)")] bool? assignStaffRole = null)
    {
        GuildSettings updated = await _settings.UpdateAsync(s =>
        {
            if (assignOnJoin is bool a) s.AssignRolesOnJoin = a;
            if (assignOnVoiceJoin is bool b) s.AssignRolesOnVoiceJoin = b;
            if (privateMeetingRole is bool c) s.PrivateMeetingRoleEnabled = c;
            if (changeNicknames is bool d) s.ChangeNicknames = d;
            if (assignStaffRole is bool e) s.AssignStaffRole = e;
        });

        _logger.LogInformation("Config: events updated by {User}", Context.User.Username);
        await RespondAsync("Events updated.", embed: BuildSettingsEmbed(updated), ephemeral: true);
    }

    [SlashCommand("roles", "Choose which roles the bot assigns.")]
    public async Task SetRolesAsync(
        [Summary("verified", "Role for members linked on VATUSA")] IRole? verified = null,
        [Summary("staff", "Role for ARTCC staff")] IRole? staff = null,
        [Summary("private-meeting", "Role given while in the private meeting voice channel")] IRole? privateMeeting = null)
    {
        List<string> warnings = [];
        foreach (IRole? role in new[] { verified, staff, privateMeeting })
        {
            if (role is not null && CantAssign(role) is string problem) warnings.Add(problem);
        }

        GuildSettings updated = await _settings.UpdateAsync(s =>
        {
            if (verified is not null) s.VerifiedRoleId = verified.Id;
            if (staff is not null) s.StaffRoleId = staff.Id;
            if (privateMeeting is not null) s.PrivateMeetingRoleId = privateMeeting.Id;
        });

        _logger.LogInformation("Config: roles updated by {User}", Context.User.Username);

        string message = warnings.Count == 0 ? "Roles updated." : "Roles updated, but:\n" + string.Join("\n", warnings.Select(w => $"⚠️ {w}"));
        await RespondAsync(message, embed: BuildSettingsEmbed(updated), ephemeral: true);
    }

    [SlashCommand("channels", "Choose the channels the bot uses.")]
    public async Task SetChannelsAsync(
        [Summary("private-meeting", "Voice channel that grants the private meeting role"), ChannelTypes(ChannelType.Voice, ChannelType.Stage)] IVoiceChannel? privateMeeting = null,
        [Summary("roles-channel", "Channel members are pointed to for /give-role"), ChannelTypes(ChannelType.Text)] ITextChannel? rolesChannel = null)
    {
        GuildSettings updated = await _settings.UpdateAsync(s =>
        {
            if (privateMeeting is not null) s.PrivateMeetingChannelId = privateMeeting.Id;
            if (rolesChannel is not null) s.RolesChannelId = rolesChannel.Id;
        });

        _logger.LogInformation("Config: channels updated by {User}", Context.User.Username);
        await RespondAsync("Channels updated.", embed: BuildSettingsEmbed(updated), ephemeral: true);
    }

    [SlashCommand("issues", "Set up GitHub issue reporting.")]
    public async Task SetIssuesAsync(
        [Summary("forum", "Forum channel with one post per GitHub issue"), ChannelTypes(ChannelType.Forum)] IForumChannel? forum = null,
        [Summary("submit-channel", "Channel for the report-an-issue buttons"), ChannelTypes(ChannelType.Text)] ITextChannel? submitChannel = null,
        [Summary("approval-channel", "Private channel where extra submissions wait for approval"), ChannelTypes(ChannelType.Text)] ITextChannel? approvalChannel = null,
        [Summary("approver-role", "Role pinged to approve submissions (anyone with Manage Server can approve too)")] IRole? approverRole = null,
        [Summary("per-hour", "Submissions a member may make per hour before the rest need approval"), MinValue(1), MaxValue(10)] int? perHour = null,
        [Summary("replies", "Which replies in issue posts become GitHub comments")] IssueReplyMode? replies = null)
    {
        // Adding the forum's tags can take a moment.
        await DeferAsync(ephemeral: true);

        GuildSettings updated = await _settings.UpdateAsync(s =>
        {
            if (forum is not null) s.IssueForumChannelId = forum.Id;
            if (submitChannel is not null) s.IssueSubmitChannelId = submitChannel.Id;
            if (approvalChannel is not null) s.IssueApprovalChannelId = approvalChannel.Id;
            if (approverRole is not null) s.IssueApproverRoleId = approverRole.Id;
            if (perHour is int limit) s.IssueSubmissionsPerHour = limit;
            if (replies is IssueReplyMode mode) s.IssueReplyMode = mode;
        });

        _logger.LogInformation("Config: issue settings updated by {User}", Context.User.Username);

        List<string> warnings = IssueSetupProblems(updated);
        if (forum is not null && _issueForum.Forum is { } forumChannel)
        {
            if (await _issueForum.EnsureTagsAsync(forumChannel) is string tagProblem) warnings.Add(tagProblem);

            // Mirror every open issue into the (possibly new) forum on the next GitHub check.
            _github.ForgetOpenIssuesETag();
        }

        string message = warnings.Count == 0 ? "Issue settings updated." : "Issue settings updated, but:\n" + string.Join("\n", warnings.Select(w => $"⚠️ {w}"));
        await FollowupAsync(message, embed: BuildSettingsEmbed(updated), ephemeral: true);
    }

    [SlashCommand("dev-task-roles", "Choose which roles can submit development tasks.")]
    public async Task ChooseDevTaskRolesAsync()
    {
        SelectMenuBuilder menu = new SelectMenuBuilder()
            .WithCustomId("admin-dev-task-roles")
            .WithType(ComponentType.RoleSelect)
            .WithPlaceholder("Roles that can submit development tasks")
            .WithMinValues(0)
            .WithMaxValues(10)
            .WithDefaultValues((_settings.Current.DevTaskRoleIds ?? []).Select(id => new SelectMenuDefaultValue(id, SelectDefaultValueType.Role)).ToArray());

        await RespondAsync("Pick the roles that can submit development tasks. Members with Manage Server always can.",
            components: new ComponentBuilder().WithSelectMenu(menu).Build(), ephemeral: true);
    }

    [ComponentInteraction("admin-dev-task-roles", ignoreGroupNames: true)]
    public async Task SetDevTaskRolesAsync(IRole[] roles)
    {
        GuildSettings updated = await _settings.UpdateAsync(s => s.DevTaskRoleIds = roles.Select(r => r.Id).ToArray());
        _logger.LogInformation("Config: dev-task roles updated by {User}", Context.User.Username);

        await ((SocketMessageComponent)Context.Interaction).UpdateAsync(m =>
        {
            m.Content = roles.Length == 0
                ? "Dev-task roles cleared. Only members with Manage Server can submit development tasks."
                : "Dev-task roles: " + string.Join(" ", roles.Select(r => r.Mention));
            m.Components = new ComponentBuilder().Build();
            m.Embed = BuildSettingsEmbed(updated);
        });
    }

    [SlashCommand("issue-panel", "Post the report-an-issue buttons in the submit channel.")]
    public async Task PostIssuePanelAsync()
    {
        if (_settings.Current.IssueSubmitChannelId is not ulong channelId || Context.Guild.GetTextChannel(channelId) is not { } channel)
        {
            await RespondAsync("Set the submit channel first with `/admin issues submit-channel:`.", ephemeral: true);
            return;
        }

        if (!Context.Guild.CurrentUser.GetPermissions(channel).SendMessages)
        {
            await RespondAsync($"I can't send messages in {channel.Mention}.", ephemeral: true);
            return;
        }

        await channel.SendMessageAsync(embed: IssueInteractionHandler.PanelEmbed(), components: IssueInteractionHandler.PanelButtons());
        _logger.LogInformation("Config: issue panel posted in #{Channel} by {User}", channel.Name, Context.User.Username);
        await RespondAsync($"Posted the buttons in {channel.Mention}. Delete any older copy of them there.", ephemeral: true);
    }

    /// <summary>Anything missing for issue reporting to work: GitHub App, channels, and the bot's permissions in them.</summary>
    private List<string> IssueSetupProblems(GuildSettings s)
    {
        List<string> problems = [];
        if (!_github.IsConfigured) problems.Add("The GitHub App isn't configured on the server (GitHub:AppId and GitHub:PrivateKeyPath).");

        SocketGuildUser me = Context.Guild.CurrentUser;

        void Check(ulong? channelId, string what, params (ChannelPermission Permission, string Name)[] needed)
        {
            if (channelId is not ulong id || Context.Guild.GetChannel(id) is not { } channel) return;
            ChannelPermissions has = me.GetPermissions(channel);
            string[] missing = needed.Where(n => !has.Has(n.Permission)).Select(n => n.Name).ToArray();
            if (missing.Length > 0) problems.Add($"In the {what} <#{id}> I'm missing: {string.Join(", ", missing)}.");
        }

        Check(s.IssueForumChannelId, "forum",
            (ChannelPermission.ViewChannel, "View Channel"),
            (ChannelPermission.SendMessages, "Create Posts"),
            (ChannelPermission.SendMessagesInThreads, "Send Messages in Posts"),
            (ChannelPermission.ManageThreads, "Manage Posts"),
            (ChannelPermission.EmbedLinks, "Embed Links"),
            (ChannelPermission.AttachFiles, "Attach Files"),
            (ChannelPermission.ReadMessageHistory, "Read Message History"));
        Check(s.IssueSubmitChannelId, "submit channel",
            (ChannelPermission.ViewChannel, "View Channel"),
            (ChannelPermission.SendMessages, "Send Messages"),
            (ChannelPermission.EmbedLinks, "Embed Links"));
        Check(s.IssueApprovalChannelId, "approval channel",
            (ChannelPermission.ViewChannel, "View Channel"),
            (ChannelPermission.SendMessages, "Send Messages"),
            (ChannelPermission.EmbedLinks, "Embed Links"),
            (ChannelPermission.AttachFiles, "Attach Files"),
            (ChannelPermission.ReadMessageHistory, "Read Message History"));

        if (s.IssueApproverRoleId is ulong roleId && Context.Guild.GetRole(roleId) is { IsMentionable: false } role
            && s.IssueApprovalChannelId is ulong approvalId && Context.Guild.GetChannel(approvalId) is { } approval
            && !me.GetPermissions(approval).MentionEveryone)
        {
            problems.Add($"I can't ping {role.Mention}: make it mentionable, or give me **Mention @everyone, @here, and All Roles** in <#{approvalId}>.");
        }

        return problems;
    }

    private string RepliesSetting(GuildSettings s) => s.IssueReplyMode switch
    {
        IssueReplyMode.MirrorAll when !_options.MessageContentIntent =>
            "⚠️ every reply, but the bot can't read message text (Bot:MessageContentIntent is off), so only \"Send to GitHub\" works",
        IssueReplyMode.MirrorAll => "every reply",
        _ => "only messages sent with \"Send to GitHub\"",
    };

    /// <summary>Returns why the bot can't assign this role, or null if it can.</summary>
    private string? CantAssign(IRole role)
    {
        if (role.Id == Context.Guild.EveryoneRole.Id) return "@everyone can't be assigned.";
        if (role.IsManaged) return $"{role.Mention} is managed by an integration and can't be assigned.";
        if (!Context.Guild.CurrentUser.GuildPermissions.ManageRoles) return "I don't have the Manage Roles permission.";
        if (role.Position >= Context.Guild.CurrentUser.Hierarchy) return $"My role must be above {role.Mention} in Server Settings → Roles.";
        return null;
    }

    private Embed BuildSettingsEmbed(GuildSettings s)
    {
        static string OnOff(bool value) => value ? "✅ On" : "⬜ Off";

        string Role(ulong? id) => id is ulong roleId
            ? Context.Guild.GetRole(roleId)?.Mention ?? "⚠️ deleted role"
            : "⚠️ not set";

        string Channel(ulong? id) => id is ulong channelId
            ? Context.Guild.GetChannel(channelId) is { } channel ? $"<#{channel.Id}>" : "⚠️ deleted channel"
            : "⚠️ not set";

        return new EmbedBuilder()
            .WithTitle($"FE-Buddy bot settings for {Context.Guild.Name}")
            .WithColor(Color.Blue)
            .AddField("Events",
                $"Assign roles on join: {OnOff(s.AssignRolesOnJoin)}\n" +
                $"Assign roles on voice join: {OnOff(s.AssignRolesOnVoiceJoin)}\n" +
                $"Private meeting role: {OnOff(s.PrivateMeetingRoleEnabled)}\n" +
                $"Change nicknames: {OnOff(s.ChangeNicknames)}\n" +
                $"Assign staff role: {OnOff(s.AssignStaffRole)}")
            .AddField("Roles",
                $"Verified: {Role(s.VerifiedRoleId)}\n" +
                $"Staff: {Role(s.StaffRoleId)}\n" +
                $"Private meeting: {Role(s.PrivateMeetingRoleId)}")
            .AddField("Channels",
                $"Private meeting voice: {Channel(s.PrivateMeetingChannelId)}\n" +
                $"Roles channel: {Channel(s.RolesChannelId)}")
            .AddField("GitHub issues",
                $"GitHub: {(_github.IsConfigured ? $"✅ {_github.Repository}" : "⚠️ GitHub App not configured")}\n" +
                $"Forum: {Channel(s.IssueForumChannelId)}\n" +
                $"Submit channel: {Channel(s.IssueSubmitChannelId)}\n" +
                $"Approval channel: {Channel(s.IssueApprovalChannelId)}\n" +
                $"Approver role: {Role(s.IssueApproverRoleId)}\n" +
                $"Dev-task roles: {(s.DevTaskRoleIds is { Length: > 0 } ids ? string.Join(" ", ids.Select(id => Role(id))) : "⚠️ not set")}\n" +
                $"Submissions per hour before approval: {s.IssueSubmissionsPerHour}\n" +
                $"Replies to GitHub: {RepliesSetting(s)}")
            .WithFooter("Change with /admin events, roles, channels, issues, dev-task-roles")
            .Build();
    }
}
