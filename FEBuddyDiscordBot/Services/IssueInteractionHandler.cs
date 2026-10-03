using FEBuddyDiscordBot.DataAccess;
using FEBuddyDiscordBot.Issues;

namespace FEBuddyDiscordBot.Services;

/// <summary>
/// The Discord side of reporting an issue. Every button, menu and modal it uses has a custom ID starting with "issue:".
/// <list type="number">
/// <item>A panel button (or /report) opens modal 1: the title plus the first questions.</item>
/// <item>The bot shows similar existing issues and a credit choice, with Continue and Cancel.</item>
/// <item>Continue opens modal 2: the rest of the questions and any files. Submitting it creates the issue
/// (or sends it for approval).</item>
/// </list>
/// </summary>
public sealed class IssueInteractionHandler
{
    private const string Prefix = "issue:";

    private readonly IssueSubmissionService _submissions;
    private readonly IssueStore _store;
    private readonly GitHubDeviceFlow _deviceFlow;
    private readonly ILogger<IssueInteractionHandler> _logger;

    public IssueInteractionHandler(IssueSubmissionService submissions, IssueStore store, GitHubDeviceFlow deviceFlow, ILogger<IssueInteractionHandler> logger)
    {
        _submissions = submissions;
        _store = store;
        _deviceFlow = deviceFlow;
        _logger = logger;
    }

    public static bool CanHandle(SocketInteraction interaction) => interaction switch
    {
        SocketMessageComponent component => component.Data.CustomId.StartsWith(Prefix, StringComparison.Ordinal),
        SocketModal modal => modal.Data.CustomId.StartsWith(Prefix, StringComparison.Ordinal),
        _ => false,
    };

    public async Task HandleAsync(SocketInteraction interaction)
    {
        try
        {
            switch (interaction)
            {
                case SocketMessageComponent component:
                    await HandleComponentAsync(component);
                    break;
                case SocketModal modal:
                    await HandleModalAsync(modal);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Issues: interaction failed for {User} ({UserId})", interaction.User.Username, interaction.User.Id);
            const string reply = "Something went wrong. It has been logged; please try again.";
            try
            {
                if (interaction.HasResponded) await interaction.FollowupAsync(reply, ephemeral: true);
                else await interaction.RespondAsync(reply, ephemeral: true);
            }
            catch (Exception replyError)
            {
                _logger.LogDebug(replyError, "Issues: could not send error reply");
            }
        }
    }

    // ---- The panel ----

    public static Embed PanelEmbed() => new EmbedBuilder()
        .WithTitle("Report an FE-BUDDY issue")
        .WithDescription(
            "Found a bug, have an idea, or spotted a docs problem? Press a button below and answer a few questions. " +
            "The bot creates a [GitHub issue](https://github.com/Nikolai558/FE-BUDDY/issues) and a forum post where you can follow it.\n\n" +
            "You need the **Verified** role. Your Discord username (or ID, if you prefer) appears on the public GitHub issue.")
        .WithColor(new Color(0x24292F))
        .Build();

    public static MessageComponent PanelButtons()
    {
        ComponentBuilder buttons = new();
        foreach (IssueTemplate template in IssueTemplate.All)
        {
            buttons.WithButton(template.Name, $"{Prefix}new:{template.Kind}", ButtonStyle.Secondary, new Emoji(template.Emoji));
        }

        return buttons.Build();
    }

    /// <summary>Step 1: check the member may submit, then open modal 1.</summary>
    public async Task StartAsync(SocketInteraction interaction, IssueKind kind)
    {
        IssueTemplate template = IssueTemplate.For(kind);
        if (interaction.User is not SocketGuildUser member)
        {
            await interaction.RespondAsync("Issues can only be reported from the server.", ephemeral: true);
            return;
        }

        if (_submissions.CantSubmit(member, template) is string problem)
        {
            await interaction.RespondAsync(problem, ephemeral: true);
            return;
        }

        await interaction.RespondWithModalAsync(BuildModal(template, 1));
    }

    // ---- Routing ----

    private async Task HandleComponentAsync(SocketMessageComponent component)
    {
        string[] parts = component.Data.CustomId.Split(':');
        switch (parts[1])
        {
            case "new":
                await StartAsync(component, Enum.Parse<IssueKind>(parts[2]));
                break;
            case "credit":
                if (_submissions.GetDraft(component.User.Id) is { } draft)
                {
                    draft.Credit = Enum.Parse<CreditStyle>(component.Data.Values.First());
                }

                await component.DeferAsync();
                break;
            case "continue":
                await ContinueAsync(component);
                break;
            case "cancel":
                _submissions.DiscardDraft(component.User.Id);
                await component.UpdateAsync(m =>
                {
                    m.Content = "Cancelled. Nothing was submitted.";
                    m.Components = new ComponentBuilder().Build();
                });
                break;
            case "approve":
            case "deny":
                await ResolveAsync(component, parts[1] == "approve", long.Parse(parts[2], System.Globalization.CultureInfo.InvariantCulture));
                break;
        }
    }

    private async Task HandleModalAsync(SocketModal modal)
    {
        string[] parts = modal.Data.CustomId.Split(':');
        switch (parts[1])
        {
            case "p1":
                await AfterFirstModalAsync(modal, Enum.Parse<IssueKind>(parts[2]));
                break;
            case "p2":
                await AfterSecondModalAsync(modal);
                break;
        }
    }

    // ---- Steps 2 and 3 ----

    /// <summary>Step 2: save the answers so far, then show similar issues and the credit choice.</summary>
    private async Task AfterFirstModalAsync(SocketModal modal, IssueKind kind)
    {
        IssueTemplate template = IssueTemplate.For(kind);
        IssueDraft draft = new()
        {
            Kind = kind,
            UserId = modal.User.Id,
            UserName = modal.User.Username,
            Title = Answer(modal, "title").Trim(),
        };
        ReadAnswers(modal, template.Page(1), draft);

        await modal.DeferAsync(ephemeral: true);

        // Members who linked GitHub are credited by their GitHub account unless they choose otherwise.
        draft.GitHubLogin = await _store.GetGitHubLoginAsync(modal.User.Id);
        if (draft.GitHubLogin is not null) draft.Credit = CreditStyle.GitHub;
        _submissions.SaveDraft(draft);
        IReadOnlyList<GitHubIssue> similar = await _submissions.FindDuplicatesAsync(draft.Title);

        string text = $"### {template.Emoji} {template.Name}: {draft.Title}\n";
        if (similar.Count == 0)
        {
            text += "No similar issues found.\n";
        }
        else
        {
            text += "**Is it one of these?** If so, add a 👍 or a reply there instead of reporting it again.\n";
            foreach (GitHubIssue issue in similar)
            {
                string where = _store.GetPostId(issue.Number) is ulong thread ? $"<#{thread}>" : $"<{issue.HtmlUrl}>";
                text += $"{(issue.IsOpen ? "🟢" : "🟣")} #{issue.Number} {issue.Title}{(issue.IsOpen ? "" : " (closed)")}: {where}\n";
            }
        }

        text += "\nChoose how you're named on the public GitHub issue, then press **Continue**.";
        if (draft.GitHubLogin is null && _deviceFlow.IsConfigured) text += " Have a GitHub account? Run `/link-github` to be credited by it next time.";

        SelectMenuBuilder credit = new SelectMenuBuilder().WithCustomId($"{Prefix}credit");
        if (draft.GitHubLogin is string login)
        {
            credit.AddOption($"My GitHub account (@{login})", nameof(CreditStyle.GitHub), "GitHub notifies you about the issue", isDefault: true);
        }

        credit
            .AddOption($"My Discord username ({modal.User.Username})", nameof(CreditStyle.DiscordName), isDefault: draft.GitHubLogin is null)
            .AddOption("My Discord user ID", nameof(CreditStyle.DiscordId), "A number instead of your name");

        MessageComponent components = new ComponentBuilder()
            .WithSelectMenu(credit)
            .WithButton("Continue", $"{Prefix}continue", ButtonStyle.Primary, row: 1)
            .WithButton("Cancel", $"{Prefix}cancel", ButtonStyle.Secondary, row: 1)
            .Build();

        await modal.FollowupAsync(text, components: components, ephemeral: true);
    }

    private async Task ContinueAsync(SocketMessageComponent component)
    {
        if (_submissions.GetDraft(component.User.Id) is not { } draft)
        {
            await component.RespondAsync("This form expired. Please start again.", ephemeral: true);
            return;
        }

        await component.RespondWithModalAsync(BuildModal(draft.Template, 2));
    }

    /// <summary>Step 3: submit.</summary>
    private async Task AfterSecondModalAsync(SocketModal modal)
    {
        if (_submissions.GetDraft(modal.User.Id) is not { } draft || modal.User is not SocketGuildUser member)
        {
            await modal.RespondAsync("This form expired. Please start again.", ephemeral: true);
            return;
        }

        ReadAnswers(modal, draft.Template.Page(2), draft);

        // Modal 2 was opened from the step-2 message, so answer by replacing that message.
        await modal.UpdateAsync(m =>
        {
            m.Content = "Submitting…";
            m.Components = new ComponentBuilder().Build();
        });

        SubmitResult result = await _submissions.SubmitAsync(draft, member, modal.Data.Attachments ?? []);
        _submissions.DiscardDraft(member.Id);

        string text = result.Outcome switch
        {
            SubmitOutcome.Created when result.ThreadId is null =>
                $"✅ Created {_submissions.Link(result)}. Its forum post will appear in a few minutes.",
            SubmitOutcome.Created => $"✅ Created {_submissions.Link(result)}. Follow it and add details in the forum post.",
            SubmitOutcome.AwaitingApproval => "📨 Thanks! You've already submitted recently, so an admin will review this one first. You'll get a DM when it's handled.",
            _ => $"⚠️ {result.Error}",
        };
        if (result.Notes is { Count: > 0 } notes) text += "\n" + string.Join("\n", notes.Select(n => $"• {n}"));

        await modal.ModifyOriginalResponseAsync(m => m.Content = text);
    }

    private async Task ResolveAsync(SocketMessageComponent component, bool approve, long submissionId)
    {
        if (component.User is not SocketGuildUser approver || !_submissions.CanApprove(approver))
        {
            await component.RespondAsync("Only admins can approve or deny submissions.", ephemeral: true);
            return;
        }

        await component.DeferAsync();
        (bool resolved, string outcome) = approve
            ? await _submissions.ApproveAsync(submissionId, approver)
            : await _submissions.DenyAsync(submissionId, approver);

        // Only the click that actually handled it updates the message and removes the buttons.
        if (!resolved)
        {
            await component.FollowupAsync($"⚠️ {outcome}", ephemeral: true);
            return;
        }

        await component.ModifyOriginalResponseAsync(m =>
        {
            m.Content = $"{component.Message.Content}\n\n{outcome}";
            m.AllowedMentions = AllowedMentions.None;
            m.Components = new ComponentBuilder().Build();
        });
    }

    // ---- Modals ----

    private static Modal BuildModal(IssueTemplate template, int page)
    {
        ModalBuilder modal = new ModalBuilder()
            .WithTitle($"{template.Name} ({page}/2)")
            .WithCustomId($"{Prefix}p{page}:{template.Kind}");

        if (page == 1)
        {
            modal.AddLabel("Title",
                new TextInputBuilder().WithCustomId("title").WithStyle(TextInputStyle.Short).WithMinLength(5).WithMaxLength(80).WithRequired(true)
                    .WithPlaceholder("A short summary"),
                "Becomes the GitHub issue and forum post title.");
        }

        foreach (IssueField field in template.Page(page))
        {
            modal.AddLabel(field.Label, BuildInput(field), field.Description);
        }

        return modal.Build();
    }

    private static IMessageComponentBuilder BuildInput(IssueField field)
    {
        switch (field.Input)
        {
            case FieldInput.Select:
            case FieldInput.MultiSelect:
                SelectMenuBuilder select = new SelectMenuBuilder()
                    .WithCustomId(field.Id)
                    .WithRequired(field.Required)
                    .WithMinValues(field.Required ? 1 : 0);
                foreach (string choice in field.Options!) select.AddOption(choice, choice);
                return select.WithMaxValues(field.Input == FieldInput.MultiSelect ? select.Options.Count : 1);

            case FieldInput.Radio:
                return new RadioGroupBuilder()
                    .WithCustomId(field.Id)
                    .WithRequired(field.Required)
                    .WithOptions(field.Options!.Select(o => new RadioGroupOptionProperties { Label = o, Value = o }).ToList());

            case FieldInput.Confirm:
                return new CheckboxGroupBuilder()
                    .WithCustomId(field.Id)
                    .WithRequired(true)
                    .WithMinValues(field.Options!.Count)
                    .WithMaxValues(field.Options!.Count)
                    .WithOptions(field.Options!.Select(o => new CheckboxGroupOptionProperties { Label = o, Value = o }).ToList());

            case FieldInput.Files:
                return new FileUploadComponentBuilder(field.Id, minValues: 0, maxValues: 10, isRequired: false);

            default:
                return new TextInputBuilder()
                    .WithCustomId(field.Id)
                    .WithStyle(field.Input == FieldInput.Paragraph ? TextInputStyle.Paragraph : TextInputStyle.Short)
                    .WithMaxLength(field.Input == FieldInput.Paragraph ? 4000 : 200)
                    .WithRequired(field.Required)
                    .WithPlaceholder(field.Placeholder);
        }
    }

    /// <summary>Copy a modal's answers into the draft, formatted as GitHub's issue forms format them.</summary>
    private static void ReadAnswers(SocketModal modal, IEnumerable<IssueField> fields, IssueDraft draft)
    {
        foreach (IssueField field in fields.Where(f => f.Input is not (FieldInput.Files or FieldInput.Confirm)))
        {
            draft.Values[field.Id] = Answer(modal, field.Id);
        }
    }

    private static string Answer(SocketModal modal, string customId)
    {
        SocketMessageComponentData? data = modal.Data.Components.FirstOrDefault(c => c.CustomId == customId);
        if (data is null) return "";
        if (data.Values is { Count: > 0 } values) return string.Join(", ", values);
        return data.Value ?? "";
    }
}
