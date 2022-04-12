using Discord;
using Discord.WebSocket;
using FEBuddyDiscordBot.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using static FEBuddyDiscordBot.Models.VatusaUserModel;

namespace FEBuddyDiscordBot.Services;
public class RoleAssignmentService
{
    private readonly IServiceProvider _services;
    private readonly IConfigurationRoot _config;
    private readonly DiscordSocketClient _discord;
    private readonly ILogger _logger;

    public RoleAssignmentService(IServiceProvider services)
    {
        _services = services;
        _config = _services.GetRequiredService<IConfigurationRoot>();
        _discord = _services.GetRequiredService<DiscordSocketClient>();
        _logger = _services.GetRequiredService<ILogger<RoleAssignmentService>>();

        _discord.UserJoined += UserJoined;

        _logger.LogInformation("Loaded: RoleAssignmentService");
    }

    private async Task UserJoined(SocketGuildUser User)
    {
        _logger.LogInformation($"User Joined: {User.Username} ({User.Id}) joined {User.Guild.Name}");
        await GiveRole(User);
    }

    public async Task GiveRole(SocketGuildUser User)
    {
        var userModel = GetVatusaUserInfo(User.Id).Result;
        var artccStaffRole = User.Guild.Roles.FirstOrDefault(x => x.Name.ToUpper() == "ARTCC STAFF");
        var verifiedRole = User.Guild.Roles.FirstOrDefault(x => x.Name.ToUpper() == "VERIFIED");
        var rolesChannel = User.Guild.Channels.FirstOrDefault(x => x.Name == "assign-my-roles");
        string newNickname = "";

        if (userModel == null)
        {
            // Todo send Not found in Vatusa embed instructions.
            await User.CreateDMChannelAsync().Result.SendMessageAsync($"You may link your account Discord account to the VATUSA Discord server here: https://vatusa.net/my/profile. Once complete, simply type the following into the FE-Buddy <#{rolesChannel.Id}> channel and your nickname and roles will be assigned appropriately: ```!Give-Role```");
            _logger.LogInformation($"Give Role: {User.Username} ({User.Id}) in {User.Guild.Name} -> Not found in VATUSA, no roles were assigned.");
            return;
        }

        if (hasArtccStaffRole(userModel))
        {
            if (User.Roles.Contains(verifiedRole))
            {
                await User.RemoveRoleAsync(verifiedRole);
                _logger.LogInformation($"Remove Role: {User.Username} ({User.Id}) in {User.Guild.Name} -> Found user in VATUSA, user also is staff; Removed {verifiedRole?.Name} role from user.");
            };

            await User.AddRoleAsync(artccStaffRole);
            _logger.LogInformation($"Give Role: {User.Username} ({User.Id}) in {User.Guild.Name} -> Found user in VATUSA, user also is staff; Assigned {artccStaffRole?.Name} role to user.");

            newNickname = $"{userModel.data.fname} {userModel.data.lname} | {userModel.data.facility} {userModel.data.roles[0].role}";
        }
        else
        {
            if (User.Roles.Contains(artccStaffRole))
            {
                await User.RemoveRoleAsync(artccStaffRole);
                _logger.LogInformation($"Remove Role: {User.Username} ({User.Id}) in {User.Guild.Name} -> Found user in VATUSA, user is no longer staff; Removed {artccStaffRole?.Name} role from user.");
            };

            await User.AddRoleAsync(verifiedRole);
            _logger.LogInformation($"Give Role: {User.Username} ({User.Id}) in {User.Guild.Name} -> Found user in VATUSA; Assigned {verifiedRole?.Name} role to user.");

            newNickname = $"{userModel.data.fname} {userModel.data.lname} | {userModel.data.facility}";
        }
        
        await User.ModifyAsync(u => u.Nickname = newNickname);
    }

    private async Task<VatusaUserData?> GetVatusaUserInfo(ulong MemberId)
    {
        string url = $"https://api.vatusa.net/v2/user/{MemberId}?d";

        using (WebClient webClient = new WebClient())
        {
            try
            {
                string jsonResponse = await webClient.DownloadStringTaskAsync(url);
                VatusaUserData? userData = JsonSerializer.Deserialize<VatusaUserData>(jsonResponse);
                return userData;
            }
            catch (WebException)
            {
                return null;
            }
        }
    }

    private bool hasArtccStaffRole(VatusaUserData userData)
    {
        if (userData.data.roles.Length >= 1)
        {
            return true;
        }
        //foreach (StaffRole role in userData.data.roles)
        //{
        //    if (new string[] {"ATM", "DATM", "FE"}.Contains(role.role))
        //    {
        //        return true;
        //    }
        //}
        return false;
    }
}
