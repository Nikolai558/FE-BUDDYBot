﻿using Discord;
using Discord.Commands;
using Discord.WebSocket;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FEBuddyDiscordBot.Modules.Owner;

[Name("Bot Owner Commands")]
[Summary("These commands only effect the hardware of the computer that the bot is running on. Therfore only the bot Owner can run these commands.")]
[RequireOwner]
public class OwnerCommands : ModuleBase
{
    private readonly IServiceProvider _services;
    private readonly IConfigurationRoot _config;
    private readonly DiscordSocketClient _discord;
    private readonly ILogger _logger;
    private readonly string _prefix;

    public OwnerCommands(IServiceProvider services)
    {
        _services = services;
        _config = _services.GetRequiredService<IConfigurationRoot>();
        _discord = _services.GetRequiredService<DiscordSocketClient>();
        _logger = _services.GetRequiredService<ILogger<OwnerCommands>>();
        _prefix = _config["prefix"];

        _logger.LogInformation("Module: Loaded OwnerCommands");
    }

    // Bot Owner Only Commands go here.

    [Command("show-count", RunMode = RunMode.Async), Name("Show-Count"), Summary("Display how many servers this bot is currently in"), Alias("show-server-count")]
    public async Task ServerCount()
    {
        IReadOnlyCollection<IGuild> guildNumber = await Context.Client.GetGuildsAsync();
        if (guildNumber.Count() > 1)
        {
            await ReplyAsync($"I am connected to {guildNumber.Count()} guild!");
        }
        else
        {
            await ReplyAsync($"I am connected to {guildNumber.Count()} guilds!");
        }
    }

    [Command("set-status", RunMode = RunMode.Async), Name("Set-Status"), Summary("Sets the status for the bot.")]
    public async Task SetStatus([Remainder] string? args = null)
    {
        await _discord.SetGameAsync(args);
    }
}
