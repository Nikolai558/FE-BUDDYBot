using Discord;
using Discord.Commands;
using Discord.WebSocket;
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
        // User joins, No roles
        // User uses command "give-roles"
        //      Bot then looks up user by discord ID in Vatusa API
        //      If user is Staff Member -> "ARTCC Staff" Role is assigned
        //      Else -> Guest is assigned
        // Rest of the Roles are Manually Assigned.

        // https://api.vatusa.net/v2/user/{cid}
        // https://api.vatusa.net/v2/user/{discordId}?d
        if (Context.Channel is IGuildChannel)
        {
            await ReplyAsync("This command is still being developed. Please try again at a later time. \nFor now, please enjoy this Guest Role.");
            await ((IGuildUser)Context.User).AddRoleAsync(Context.Guild.Roles.FirstOrDefault(x => x.Name == "Guest"));
        }
        else
        {
            await ReplyAsync("I can not assign you roles inside of a Direct Message. Please go to the appropriate server channel and use the command again.");
        }
    }
}
