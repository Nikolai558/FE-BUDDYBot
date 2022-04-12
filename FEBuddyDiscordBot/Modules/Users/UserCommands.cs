using Discord;
using Discord.Commands;
using Discord.WebSocket;
using FEBuddyDiscordBot.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace FEBuddyDiscordBot.Modules.Users;

[Name("User Commands")]
[Summary("Helpful commands for All users in the Discord server!")]
public class UserCommands : ModuleBase
{
    private readonly IServiceProvider _services;
    private readonly IConfigurationRoot _config;
    private readonly DiscordSocketClient _discord;
    private readonly ILogger _logger;
    private readonly string _prefix;

    public UserCommands(IServiceProvider services)
    {
        _services = services;
        _config = _services.GetRequiredService<IConfigurationRoot>();
        _discord = _services.GetRequiredService<DiscordSocketClient>();
        _logger = _services.GetRequiredService<ILogger<UserCommands>>();
        _prefix = _config["prefix"];

        _logger.LogInformation("Module: Loaded UserCommands");
    }

    // Discord Server User commands go here.

    [Command("give-roles", RunMode = RunMode.Async), Alias(new string[] {"gr", "give-role", "assign-roles", "assign-role" }), Name("Give-Roles"), Summary("Give Discord Server Roles Depending on VATUSA Status.")]
    public async Task AssignRoles()
    {
        if (Context.Channel is IGuildChannel)
        {
            await _services.GetRequiredService<RoleAssignmentService>().GiveRole((SocketGuildUser)Context.User);
            //await ReplyAsync("This command is still being developed. Please try again at a later time. \nFor now, please enjoy this Guest Role.");
        }
        else
        {
            await ReplyAsync("I can not assign you roles inside of a Direct Message. Please go to the appropriate server channel and use the command again." +
                "\nListen... The messages you send me will always remain between just you and I... This is because I am your buddy, and it is important that friends keep secrets... but mostly because I'm not real and nobody is monitoring these messages. \n\n(private message and admin of the discord to get appropriate roles assigned or... (VATUSA stuff))");
        }
    }
}
