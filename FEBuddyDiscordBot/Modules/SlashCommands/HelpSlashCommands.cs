using System.Text;
using FEBuddyDiscordBot.DataAccess;
using FEBuddyDiscordBot.Models;

namespace FEBuddyDiscordBot.Modules.SlashCommands;

/// <summary>
/// /help: the bot's commands the member can use, with what each one does. Each command's own permission checks decide
/// whether it's listed, so new commands show up, for the right people, without changing this.
/// </summary>
public sealed class HelpSlashCommands : InteractionModuleBase<SocketInteractionContext>
{
    private const string DocsUrl = "https://github.com/Nikolai558/FE-BUDDYBot/blob/main/Docs/README.md#commands";

    /// <summary>Sections in the order they're shown.</summary>
    private static readonly string[] Sections = ["Everyone", "Staff", "Admin", "Bot owner"];

    /// <summary>Message actions have no description of their own.</summary>
    private static readonly Dictionary<string, string> MessageActionDescriptions = new()
    {
        ["Send to GitHub"] = "Copy one of your replies in an issue post to its GitHub issue.",
    };

    private readonly InteractionService _interactions;
    private readonly IServiceProvider _services;
    private readonly GuildSettingsStore _settings;

    public HelpSlashCommands(InteractionService interactions, IServiceProvider services, GuildSettingsStore settings)
    {
        _interactions = interactions;
        _services = services;
        _settings = settings;
    }

    [SlashCommand("help", "List the bot's commands you can use, and what each one does.")]
    public async Task HelpAsync()
    {
        SocketGuildUser user = (SocketGuildUser)Context.User;
        GuildSettings settings = _settings.Current;
        if (settings.VerifiedRoleId is ulong verifiedId && user.Roles.All(r => r.Id != verifiedId) && !user.GuildPermissions.ManageGuild)
        {
            string where = settings.RolesChannelId is ulong channelId ? $" in <#{channelId}>" : "";
            await RespondAsync($"You need the Verified role to use `/help`. Run `/give-role`{where} first.", ephemeral: true);
            return;
        }

        await DeferAsync(ephemeral: true);

        List<(string Section, string Line)> entries = [];
        foreach (SlashCommandInfo command in _interactions.SlashCommands.Where(c => c.Name != "help"))
        {
            if (!(await command.CheckPreconditionsAsync(Context, _services)).IsSuccess) continue;
            string name = string.Join(" ", new[] { command.Module.SlashGroupName, command.Name }.Where(n => !string.IsNullOrEmpty(n)));
            entries.Add((SectionOf(command.Module), $"`/{name}`: {command.Description}"));
        }

        foreach (ContextCommandInfo command in _interactions.ContextCommands)
        {
            if (!(await command.CheckPreconditionsAsync(Context, _services)).IsSuccess) continue;
            string description = MessageActionDescriptions.GetValueOrDefault(command.Name, "A message action.");
            entries.Add((SectionOf(command.Module), $"**{command.Name}**: {description} Right-click (or long-press) a message, then **Apps**."));
        }

        List<string> blocks = Sections
            .Select(section => (section, lines: entries.Where(e => e.Section == section).Select(e => e.Line).Order(StringComparer.Ordinal).ToList()))
            .Where(s => s.lines.Count > 0)
            .Select(s => $"**{s.section}**\n{string.Join("\n", s.lines)}")
            .ToList();
        blocks.Add($"-# Some commands only work in certain channels. Full details: [command docs](<{DocsUrl}>).");

        await FollowupAsync(embeds: BuildEmbeds(blocks), ephemeral: true);
    }

    /// <summary>Which section a command is listed under: its group, or Everyone for ungrouped commands anyone can run.</summary>
    private static string SectionOf(ModuleInfo module) => module.SlashGroupName switch
    {
        "admin" => "Admin",
        "owner" => "Bot owner",
        "staff" => "Staff",
        _ => module.Preconditions.Count > 0 ? "Staff" : "Everyone",
    };

    /// <summary>The sections as embeds, starting a new one when the next section won't fit (4,096 characters each).</summary>
    private static Embed[] BuildEmbeds(List<string> blocks)
    {
        List<Embed> embeds = [];
        StringBuilder text = new();

        void Flush()
        {
            if (text.Length == 0) return;
            EmbedBuilder embed = new EmbedBuilder().WithColor(Color.Blue).WithDescription(text.ToString());
            if (embeds.Count == 0) embed.WithTitle("FE-Buddy bot commands you can use");
            embeds.Add(embed.Build());
            text.Clear();
        }

        foreach (string block in blocks)
        {
            if (text.Length > 0 && text.Length + block.Length + 2 > 4000) Flush();
            if (text.Length > 0) text.Append("\n\n");
            text.Append(block);
        }

        Flush();
        return [.. embeds];
    }
}
