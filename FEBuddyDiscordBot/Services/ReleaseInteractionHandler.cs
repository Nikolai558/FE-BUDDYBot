using FEBuddyDiscordBot.DataAccess;
using FEBuddyDiscordBot.Models;
using FEBuddyDiscordBot.Announcements;

namespace FEBuddyDiscordBot.Services;

/// <summary>
/// The Post, Edit and Cancel buttons under a release announcement preview (see <see cref="ReleaseWatchService"/>), and the
/// Edit form. The preview message's own text is the announcement, so an edit is saved by editing the preview.
/// Only members with Manage Server can use them.
/// </summary>
public sealed class ReleaseInteractionHandler
{
    private const string TextInputId = "text";

    private readonly ReleaseStore _store;
    private readonly GuildSettingsStore _settings;
    private readonly ILogger<ReleaseInteractionHandler> _logger;

    public ReleaseInteractionHandler(ReleaseStore store, GuildSettingsStore settings, ILogger<ReleaseInteractionHandler> logger)
    {
        _store = store;
        _settings = settings;
        _logger = logger;
    }

    public static bool CanHandle(SocketInteraction interaction) => interaction switch
    {
        SocketMessageComponent component => component.Data.CustomId.StartsWith(ReleasePreview.Prefix, StringComparison.Ordinal),
        SocketModal modal => modal.Data.CustomId.StartsWith(ReleasePreview.Prefix, StringComparison.Ordinal),
        _ => false,
    };

    public async Task HandleAsync(SocketInteraction interaction)
    {
        try
        {
            // "release:<action>:<tag>"
            string customId = interaction is SocketModal m ? m.Data.CustomId : ((SocketMessageComponent)interaction).Data.CustomId;
            string[] parts = customId.Split(':', 3);
            if (parts.Length < 3) return;
            string action = parts[1], tag = parts[2];

            if (interaction.User is not SocketGuildUser admin || !admin.GuildPermissions.ManageGuild)
            {
                await interaction.RespondAsync("Only admins (Manage Server) can post, edit or cancel release announcements.", ephemeral: true);
                return;
            }

            if (await _store.GetAsync(tag) is not { } release)
            {
                await interaction.RespondAsync("I don't know this release any more.", ephemeral: true);
                return;
            }

            if (release.Status != ReleaseStatus.Pending)
            {
                await interaction.RespondAsync($"This announcement was already handled ({release.Status.ToString().ToLowerInvariant()}).", ephemeral: true);
                return;
            }

            switch (interaction, action)
            {
                case (SocketMessageComponent component, "post"):
                    await PostAsync(component, admin, release);
                    break;
                case (SocketMessageComponent component, "edit"):
                    await component.RespondWithModalAsync(BuildEditModal(tag, component.Message.Content));
                    break;
                case (SocketMessageComponent component, "cancel"):
                    await CancelAsync(component, admin, release);
                    break;
                case (SocketModal modal, "edit"):
                    await SaveEditAsync(modal, admin, release);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Releases: interaction failed for {User} ({UserId})", interaction.User.Username, interaction.User.Id);
            const string reply = "Something went wrong. It has been logged; please try again.";
            try
            {
                if (interaction.HasResponded) await interaction.FollowupAsync(reply, ephemeral: true);
                else await interaction.RespondAsync(reply, ephemeral: true);
            }
            catch (Exception replyError)
            {
                _logger.LogDebug(replyError, "Releases: could not send error reply");
            }
        }
    }

    private async Task PostAsync(SocketMessageComponent component, SocketGuildUser admin, ReleaseRecord release)
    {
        GuildSettings settings = _settings.Current;
        if (settings.ReleaseChannelId is not ulong channelId || admin.Guild.GetTextChannel(channelId) is not { } channel)
        {
            await component.RespondAsync("Set the announcement channel first with `/admin releases channel:`.", ephemeral: true);
            return;
        }

        if (!await _store.TryMoveAsync(release.Tag, ReleaseStatus.Pending, ReleaseStatus.Posting, admin.Id))
        {
            await component.RespondAsync("Another admin is already handling this announcement.", ephemeral: true);
            return;
        }

        await component.DeferAsync();
        IUserMessage posted;
        try
        {
            SocketRole? role = settings.ReleasePingRoleId is ulong roleId ? admin.Guild.GetRole(roleId) : null;
            string text = component.Message.Content;
            posted = role is null
                ? await channel.SendMessageAsync(text, allowedMentions: AllowedMentions.None)
                : await channel.SendMessageAsync($"{text}\n\n{role.Mention}", allowedMentions: new AllowedMentions { RoleIds = [role.Id] },
                    components: ReleasePreview.PingToggleButton());
        }
        catch
        {
            await _store.TryMoveAsync(release.Tag, ReleaseStatus.Posting, ReleaseStatus.Pending);
            throw;
        }

        await _store.MarkPostedAsync(release.Tag, channel.Id, posted.Id);
        _logger.LogInformation("Releases: {User} posted the FE-BUDDY {Version} announcement in #{Channel}", admin.Username, Version(release), channel.Name);

        // Replace the preview with a short record of who posted it and where.
        await component.Message.DeleteAsync();
        await component.Channel.SendMessageAsync(
            $"Release announcement for FE-BUDDY **{Version(release)}** accepted by {admin.Mention} and posted to {channel.Mention}: {posted.GetJumpUrl()}",
            allowedMentions: AllowedMentions.None);
    }

    /// <summary>Delete the preview and leave a note that the release needs announcing by hand.</summary>
    private async Task CancelAsync(SocketMessageComponent component, SocketGuildUser admin, ReleaseRecord release)
    {
        if (!await _store.TryMoveAsync(release.Tag, ReleaseStatus.Pending, ReleaseStatus.Cancelled, admin.Id))
        {
            await component.RespondAsync("Another admin is already handling this announcement.", ephemeral: true);
            return;
        }

        await component.DeferAsync();
        await component.Message.DeleteAsync();

        string link = release.Url is null ? "" : $" Release: <{release.Url}>";
        await component.Channel.SendMessageAsync(
            $"Manual release notification required for FE-BUDDY **{Version(release)}**: {admin.Mention} cancelled the automatic announcement.{link}",
            allowedMentions: AllowedMentions.None);
        _logger.LogInformation("Releases: {User} cancelled the FE-BUDDY {Version} announcement", admin.Username, Version(release));
    }

    private async Task SaveEditAsync(SocketModal modal, SocketGuildUser admin, ReleaseRecord release)
    {
        string text = modal.Data.Components.FirstOrDefault(c => c.CustomId == TextInputId)?.Value?.Trim() ?? "";
        if (text.Length == 0)
        {
            await modal.RespondAsync("The announcement can't be empty.", ephemeral: true);
            return;
        }

        // The form was opened from the preview's Edit button, so this edits the preview.
        await modal.UpdateAsync(m =>
        {
            m.Content = text;
            m.AllowedMentions = AllowedMentions.None;
        });
        _logger.LogInformation("Releases: {User} edited the FE-BUDDY {Version} announcement", admin.Username, Version(release));
    }

    private static Modal BuildEditModal(string tag, string currentText) => new ModalBuilder()
        .WithTitle("Edit the release announcement")
        .WithCustomId($"{ReleasePreview.Prefix}edit:{tag}")
        .AddLabel("Announcement",
            new TextInputBuilder()
                .WithCustomId(TextInputId)
                .WithStyle(TextInputStyle.Paragraph)
                .WithMinLength(1)
                .WithMaxLength(ReleaseAnnouncement.MaxLength)
                .WithRequired(true)
                .WithValue(currentText.Length > ReleaseAnnouncement.MaxLength ? currentText[..ReleaseAnnouncement.MaxLength] : currentText),
            "Discord markdown. The ping is added below it when posted.")
        .Build();

    private static string Version(ReleaseRecord release) => release.Tag.TrimStart('v', 'V');
}
