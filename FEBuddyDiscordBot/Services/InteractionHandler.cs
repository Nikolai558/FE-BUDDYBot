namespace FEBuddyDiscordBot.Services;

/// <summary>
/// Loads the slash command modules and routes Discord interactions to them.
/// </summary>
public sealed class InteractionHandler
{
    private readonly DiscordSocketClient _discord;
    private readonly InteractionService _interactions;
    private readonly IServiceProvider _services;
    private readonly ILogger<InteractionHandler> _logger;

    public InteractionHandler(DiscordSocketClient discord, InteractionService interactions, IServiceProvider services, ILogger<InteractionHandler> logger)
    {
        _discord = discord;
        _interactions = interactions;
        _services = services;
        _logger = logger;
    }

    public async Task InitializeAsync()
    {
        await _interactions.AddModulesAsync(typeof(InteractionHandler).Assembly, _services);

        _discord.InteractionCreated += HandleInteractionAsync;
        _interactions.InteractionExecuted += OnInteractionExecutedAsync;
    }

    private async Task HandleInteractionAsync(SocketInteraction interaction)
    {
        try
        {
            SocketInteractionContext context = new(_discord, interaction);
            await _interactions.ExecuteCommandAsync(context, _services);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Interaction: failed to execute interaction {InteractionId}", interaction.Id);
        }
    }

    private async Task OnInteractionExecutedAsync(ICommandInfo? command, IInteractionContext context, IResult result)
    {
        string name = command is SlashCommandInfo slash
            ? "/" + string.Join(" ", new[] { slash.Module.SlashGroupName, slash.Name }.Where(n => !string.IsNullOrEmpty(n)))
            : command?.Name ?? "unknown";

        if (result.IsSuccess)
        {
            _logger.LogInformation("Command: {User} ({UserId}) ran {Command}", context.User.Username, context.User.Id, name);
            return;
        }

        string reply;
        switch (result.Error)
        {
            case InteractionCommandError.UnmetPrecondition:
                _logger.LogInformation("Command: {User} ({UserId}) was denied {Command}: {Reason}", context.User.Username, context.User.Id, name, result.ErrorReason);
                reply = $"You can't use this command: {result.ErrorReason}";
                break;
            case InteractionCommandError.Exception when result is ExecuteResult { Exception: { } ex }:
                _logger.LogError(ex, "Command: {Command} failed for {User} ({UserId})", name, context.User.Username, context.User.Id);
                reply = "Something went wrong running that command. It has been logged.";
                break;
            default:
                _logger.LogWarning("Command: {Command} failed for {User} ({UserId}): {Error} {Reason}", name, context.User.Username, context.User.Id, result.Error, result.ErrorReason);
                reply = "Something went wrong running that command.";
                break;
        }

        try
        {
            if (context.Interaction.HasResponded) await context.Interaction.FollowupAsync(reply, ephemeral: true);
            else await context.Interaction.RespondAsync(reply, ephemeral: true);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Command: could not send error reply for {Command}", name);
        }
    }
}
