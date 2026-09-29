using FEBuddyDiscordBot.DataAccess;
using FEBuddyDiscordBot.Issues;
using FEBuddyDiscordBot.Services;

namespace FEBuddyDiscordBot.Modules.SlashCommands;

/// <summary>
/// /report (the same flow as the buttons in the submit channel), the "Send to GitHub" message action,
/// and linking a GitHub account.
/// </summary>
public sealed class IssueSlashCommands : InteractionModuleBase<SocketInteractionContext>
{
    private readonly IssueInteractionHandler _issues;
    private readonly IssueReplyService _replies;
    private readonly IssueSubmissionService _submissions;
    private readonly IssueStore _store;
    private readonly GitHubDeviceFlow _deviceFlow;
    private readonly ILogger<IssueSlashCommands> _logger;

    public IssueSlashCommands(
        IssueInteractionHandler issues,
        IssueReplyService replies,
        IssueSubmissionService submissions,
        IssueStore store,
        GitHubDeviceFlow deviceFlow,
        ILogger<IssueSlashCommands> logger)
    {
        _issues = issues;
        _replies = replies;
        _submissions = submissions;
        _store = store;
        _deviceFlow = deviceFlow;
        _logger = logger;
    }

    [SlashCommand("report", "Report an FE-BUDDY bug, request a feature, or flag a docs problem. Needs the Verified role.")]
    public async Task ReportAsync([Summary("type", "What are you reporting?")] IssueKind type)
    {
        await _issues.StartAsync(Context.Interaction, type);
    }

    [MessageCommand("Send to GitHub")]
    public async Task SendToGitHubAsync(IMessage message)
    {
        if (_replies.IssueFor(Context.Channel) is not int issue)
        {
            await RespondAsync("This only works on messages in an issue's forum post.", ephemeral: true);
            return;
        }

        if (message is not IUserMessage userMessage || message.Author.IsBot || message.Id == Context.Channel.Id)
        {
            await RespondAsync("Only members' replies can be sent to GitHub.", ephemeral: true);
            return;
        }

        if (message.Author.Id != Context.User.Id && !(Context.User is SocketGuildUser member && _submissions.CanApprove(member)))
        {
            await RespondAsync("You can only send your own messages to GitHub.", ephemeral: true);
            return;
        }

        await DeferAsync(ephemeral: true);
        try
        {
            string? url = await _replies.SendAsync(userMessage, issue);
            await FollowupAsync(url is null ? "That message is already on GitHub." : $"Sent to GitHub: <{url}>", ephemeral: true);
        }
        catch (GitHubException ex)
        {
            _logger.LogWarning("Issues: Send to GitHub failed for message {MessageId}: {Error}", message.Id, ex.Message);
            await FollowupAsync("GitHub didn't accept it. Please try again in a few minutes.", ephemeral: true);
        }
    }

    [SlashCommand("link-github", "Link your GitHub account, so issues and replies you send from Discord are credited to it.")]
    public async Task LinkGitHubAsync()
    {
        if (!_deviceFlow.IsConfigured)
        {
            await RespondAsync("Linking GitHub accounts isn't set up yet.", ephemeral: true);
            return;
        }

        DeviceCode code;
        try
        {
            code = await _deviceFlow.StartAsync();
        }
        catch (Exception ex) when (ex is GitHubException or HttpRequestException)
        {
            _logger.LogWarning("Issues: couldn't start GitHub linking: {Error}", ex.Message);
            await RespondAsync("GitHub didn't answer. Please try again in a few minutes.", ephemeral: true);
            return;
        }

        await RespondAsync(
            $"1. Open **{code.VerificationUri}**\n" +
            $"2. Enter the code **`{code.UserCode}`**\n" +
            "3. Approve **FE-BUDDY Bot**\n\n" +
            "The bot only reads your GitHub username, then throws the sign-in away. " +
            $"The code works for {code.ExpiresIn / 60} minutes; I'll reply here when it's done.",
            components: new ComponentBuilder().WithButton("Open GitHub", style: ButtonStyle.Link, url: code.VerificationUri).Build(),
            ephemeral: true);

        string? login;
        try
        {
            login = await _deviceFlow.WaitForLoginAsync(code);
        }
        catch (Exception ex) when (ex is GitHubException or HttpRequestException)
        {
            _logger.LogWarning("Issues: GitHub linking failed for {User}: {Error}", Context.User.Username, ex.Message);
            login = null;
        }

        if (login is not null)
        {
            await _store.SetGitHubLoginAsync(Context.User.Id, login);
            _logger.LogInformation("Issues: {User} ({UserId}) linked GitHub account {Login}", Context.User.Username, Context.User.Id, login);
        }

        try
        {
            await FollowupAsync(login is null
                    ? "The code expired or wasn't approved. Run `/link-github` to try again."
                    : $"✅ Linked to GitHub account **{login}**. Your replies in issue posts now show it, and you can pick it as your credit when reporting an issue.",
                ephemeral: true);
        }
        catch (Exception ex)
        {
            // Discord only allows follow-ups for 15 minutes, the same time the code lasts.
            _logger.LogDebug(ex, "Issues: couldn't send the linking result to {User}", Context.User.Username);
        }
    }

    [SlashCommand("unlink-github", "Stop crediting your GitHub account on issues and replies you send from Discord.")]
    public async Task UnlinkGitHubAsync()
    {
        string? login = await _store.GetGitHubLoginAsync(Context.User.Id);
        await _store.SetGitHubLoginAsync(Context.User.Id, null);
        await RespondAsync(login is null ? "No GitHub account was linked." : $"Unlinked GitHub account **{login}**.", ephemeral: true);
    }
}
