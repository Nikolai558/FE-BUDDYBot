using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using Discord.Net;
using FEBuddyDiscordBot.DataAccess;

namespace FEBuddyDiscordBot.Modules.SlashCommands;

/// <summary>
/// Commands for members who can manage messages.
/// </summary>
[DefaultMemberPermissions(GuildPermission.ManageMessages)]
[RequireUserPermission(GuildPermission.ManageMessages)]
public sealed class ModerationSlashCommands : InteractionModuleBase<SocketInteractionContext>
{
    public const int MaxAmount = 100;

    private const int PageSize = 100;

    // Discord only bulk-deletes messages under 14 days old. Older ones are deleted one at a time, which is much slower.
    // The margin covers the time between fetching a page and deleting it.
    internal static readonly TimeSpan BulkDeleteMaxAge = TimeSpan.FromDays(14) - TimeSpan.FromMinutes(5);

    // One clear per channel at a time.
    private static readonly ConcurrentDictionary<ulong, byte> Running = new();

    private readonly GuildSettingsStore _settings;
    private readonly ILogger<ModerationSlashCommands> _logger;

    public ModerationSlashCommands(GuildSettingsStore settings, ILogger<ModerationSlashCommands> logger)
    {
        _settings = settings;
        _logger = logger;
    }

    [SlashCommand("clear-messages", "Delete the latest 1-100 messages in this channel, or all of them.")]
    public async Task ClearMessagesAsync(
        [Summary("amount", "How many of the latest messages to delete (1-100), or \"all\"")] string amount,
        [Summary("keep-pinned", "Leave pinned messages alone (default: True)")] bool keepPinned = true)
    {
        if (!TryParseAmount(amount, out int? count))
        {
            await RespondAsync($"`amount` must be a number from 1 to {MaxAmount}, or `all`.", ephemeral: true);
            return;
        }

        (ITextChannel? channel, string? problem) = Target();
        if (channel is null)
        {
            await RespondAsync(problem, ephemeral: true);
            return;
        }

        if (count is int n)
        {
            await DeferAsync(ephemeral: true);
            await ClearAsync(channel, n, keepPinned);
            return;
        }

        // Everything can't be undone, so ask first.
        MessageComponent buttons = new ComponentBuilder()
            .WithButton("Delete all messages", $"clear-messages:all:{(keepPinned ? "keep-pinned" : "include-pinned")}", ButtonStyle.Danger)
            .WithButton("Cancel", "clear-messages:cancel", ButtonStyle.Secondary)
            .Build();

        await RespondAsync(
            $"Delete **every** message in {channel.Mention}{(keepPinned ? " except pinned ones" : ", including pinned ones")}? This can't be undone.",
            components: buttons, ephemeral: true);
    }

    [ComponentInteraction("clear-messages:all:*", ignoreGroupNames: true)]
    public async Task ConfirmClearAllAsync(string pinned)
    {
        SocketMessageComponent component = (SocketMessageComponent)Context.Interaction;

        // Check again: permissions may have changed since the question was asked.
        (ITextChannel? channel, string? problem) = Target();
        await component.UpdateAsync(m =>
        {
            m.Content = channel is null ? problem : $"Deleting every message in {channel.Mention}...";
            m.Components = new ComponentBuilder().Build();
        });

        if (channel is not null) await ClearAsync(channel, count: null, keepPinned: pinned == "keep-pinned");
    }

    [ComponentInteraction("clear-messages:cancel", ignoreGroupNames: true)]
    public async Task CancelClearAsync()
    {
        await ((SocketMessageComponent)Context.Interaction).UpdateAsync(m =>
        {
            m.Content = "Cancelled. Nothing was deleted.";
            m.Components = new ComponentBuilder().Build();
        });
    }

    /// <summary>"1" to "100", or "all" (null).</summary>
    internal static bool TryParseAmount(string input, out int? count)
    {
        count = null;
        input = input.Trim();
        if (input.Equals("all", StringComparison.OrdinalIgnoreCase)) return true;

        if (int.TryParse(input, NumberStyles.None, CultureInfo.InvariantCulture, out int n) && n is >= 1 and <= MaxAmount)
        {
            count = n;
            return true;
        }

        return false;
    }

    /// <summary>Splits messages into those Discord can bulk-delete and those that must be deleted one at a time.</summary>
    internal static (List<T> Recent, List<T> Old) SplitByAge<T>(IEnumerable<T> messages, DateTimeOffset now) where T : ISnowflakeEntity
    {
        List<T> recent = [], old = [];
        foreach (T message in messages)
        {
            (now - message.CreatedAt < BulkDeleteMaxAge ? recent : old).Add(message);
        }

        return (recent, old);
    }

    /// <summary>The channel the command was run in, or why it can't be cleared.</summary>
    private (ITextChannel? Channel, string? Problem) Target()
    {
        if (Context.Channel is not ITextChannel channel) return (null, "This only works in text channels and threads.");

        // Deleting replies in an issue post deletes their GitHub comments too.
        IGuildChannel permissionsFrom = channel;
        if (channel is SocketThreadChannel thread)
        {
            if (thread.ParentChannel.Id == _settings.Current.IssueForumChannelId)
            {
                return (null, "Issue posts can't be cleared: deleting replies there deletes their comments on GitHub too.");
            }

            permissionsFrom = thread.ParentChannel;
        }

        ChannelPermissions has = Context.Guild.CurrentUser.GetPermissions(permissionsFrom);
        if (!has.ViewChannel || !has.ReadMessageHistory || !has.ManageMessages)
        {
            return (null, $"I need View Channel, Read Message History and Manage Messages in {channel.Mention} to clear it.");
        }

        return (channel, null);
    }

    /// <param name="count">How many of the latest messages to look at, or null for all of them.</param>
    private async Task ClearAsync(ITextChannel channel, int? count, bool keepPinned)
    {
        if (!Running.TryAdd(channel.Id, 0))
        {
            await ReportAsync("A clear is already running in this channel. Wait for it to finish.");
            return;
        }

        try
        {
            string what = count is int n ? $"the last {n}" : "all";
            _logger.LogInformation("Clear Messages: {User} ({UserId}) started deleting {What} messages in #{Channel}{Pinned}",
                Context.User.Username, Context.User.Id, what, channel.Name, keepPinned ? " (keeping pinned)" : "");

            int deleted = 0, pinnedKept = 0, failed = 0;
            int remaining = count ?? int.MaxValue;
            ulong? before = null;
            DateTimeOffset lastProgress = DateTimeOffset.UtcNow;

            // Page backwards by message ID, so kept (pinned or undeletable) messages aren't fetched again.
            while (remaining > 0)
            {
                int take = Math.Min(PageSize, remaining);
                List<IMessage> page = [.. await (before is ulong id
                    ? channel.GetMessagesAsync(id, Direction.Before, take)
                    : channel.GetMessagesAsync(take)).FlattenAsync()];
                if (page.Count == 0) break;

                before = page.Min(m => m.Id);
                remaining -= page.Count;

                List<IMessage> toDelete = keepPinned ? page.Where(m => !m.IsPinned).ToList() : page;
                pinnedKept += page.Count - toDelete.Count;

                (int pageDeleted, int pageFailed) = await DeleteAsync(channel, toDelete);
                deleted += pageDeleted;
                failed += pageFailed;

                if (DateTimeOffset.UtcNow - lastProgress > TimeSpan.FromSeconds(10))
                {
                    lastProgress = DateTimeOffset.UtcNow;
                    await ReportAsync($"Deleting messages in {channel.Mention}... {deleted} so far.", progress: true);
                }
            }

            _logger.LogInformation("Clear Messages: {User} ({UserId}) deleted {Deleted} messages in #{Channel}; kept {Pinned} pinned; {Failed} couldn't be deleted",
                Context.User.Username, Context.User.Id, deleted, channel.Name, pinnedKept, failed);

            string summary = $"Deleted {deleted} {(deleted == 1 ? "message" : "messages")} in {channel.Mention}.";
            if (pinnedKept > 0) summary += $" Kept {pinnedKept} pinned.";
            if (failed > 0) summary += $" {failed} couldn't be deleted (see the bot log).";
            await ReportAsync(summary);
        }
        finally
        {
            Running.TryRemove(channel.Id, out _);
        }
    }

    private async Task<(int Deleted, int Failed)> DeleteAsync(ITextChannel channel, List<IMessage> messages)
    {
        (List<IMessage> recent, List<IMessage> old) = SplitByAge(messages, DateTimeOffset.UtcNow);
        int deleted = 0, failed = 0;

        // Bulk delete takes 2 to 100 messages. If it's refused (e.g. one of them can't be deleted), go one by one.
        if (recent.Count >= 2)
        {
            try
            {
                await channel.DeleteMessagesAsync(recent);
                deleted += recent.Count;
                recent = [];
            }
            catch (HttpException ex)
            {
                _logger.LogDebug("Clear Messages: bulk delete in #{Channel} failed, deleting one at a time: {Error}", channel.Name, ex.Message);
            }
        }

        foreach (IMessage message in recent.Concat(old))
        {
            try
            {
                await channel.DeleteMessageAsync(message);
                deleted++;
            }
            catch (HttpException ex) when (ex.HttpCode == HttpStatusCode.NotFound)
            {
                // Already deleted by someone else.
            }
            catch (HttpException ex)
            {
                _logger.LogWarning("Clear Messages: could not delete message {MessageId} in #{Channel}: {Error}", message.Id, channel.Name, ex.Message);
                failed++;
            }
        }

        return (deleted, failed);
    }

    /// <summary>
    /// Shows a status in the command's private reply. The reply can only be edited for 15 minutes; after that,
    /// progress is dropped and the final summary goes to the member's DMs.
    /// </summary>
    private async Task ReportAsync(string message, bool progress = false)
    {
        try
        {
            await Context.Interaction.ModifyOriginalResponseAsync(m =>
            {
                m.Content = message;
                m.Components = new ComponentBuilder().Build();
            });
        }
        catch (HttpException) when (progress)
        {
        }
        catch (HttpException)
        {
            try
            {
                await Context.User.SendMessageAsync(message);
            }
            catch (HttpException ex)
            {
                _logger.LogInformation("Clear Messages: could not tell {User} ({UserId}) the result: {Error}", Context.User.Username, Context.User.Id, ex.Message);
            }
        }
    }
}
