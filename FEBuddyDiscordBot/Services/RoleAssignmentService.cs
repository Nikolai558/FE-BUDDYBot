using FEBuddyDiscordBot.DataAccess;
using FEBuddyDiscordBot.DataAccess.DB;
using FEBuddyDiscordBot.Models;
using static FEBuddyDiscordBot.Models.VatusaUserModel;

namespace FEBuddyDiscordBot.Services;

/// <summary>
/// Assign roles to users, dependant on guilds configuration inside of the database
/// </summary>
public class RoleAssignmentService
{
    // Dependency Injection services needed 
    private readonly IServiceProvider _services;
    private readonly DiscordSocketClient _discord;
    private readonly ILogger _logger;
    private readonly VatusaApi _vatusaApi;
    private readonly IMongoGuildData _guildData;

    /// <summary>
    /// Constructor for the Role Assignment Service
    /// </summary>
    /// <param name="services">Dependency Injection Service Provider</param>
    public RoleAssignmentService(IServiceProvider services)
    {
        _services = services;
        _discord = _services.GetRequiredService<DiscordSocketClient>();
        _logger = _services.GetRequiredService<ILogger<RoleAssignmentService>>();
        _vatusaApi = _services.GetRequiredService<VatusaApi>();
        _guildData = _services.GetRequiredService<IMongoGuildData>();

        // Handle discord events.
        _discord.UserJoined += UserJoined;
        _discord.UserVoiceStateUpdated += UserConnectedToVoice;

        _logger.LogDebug("Loaded: RoleAssignmentService");
    }

    /// <summary>
    /// This function is called anytime a user joins or leaves a discord voice channel.
    /// </summary>
    /// <param name="User">Socket user from Discord</param>
    /// <param name="CurrentVoiceState">Current voice state of that user</param>
    /// <param name="NewVoiceState">New voice state of that user</param>
    /// <returns>None</returns>
    private async Task UserConnectedToVoice(SocketUser User, SocketVoiceState CurrentVoiceState, SocketVoiceState NewVoiceState)
    {
        SocketGuildUser _user = (SocketGuildUser)User;
        GuildModel guild = await _guildData.GetGuildAsync(_user.Guild.Id);

        if (_user == null) return;

        if (guild.Settings.AutoAssignRoles_OnVoiceChannelJoin)
        {
            if (NewVoiceState.VoiceChannel != null) await GiveRole(_user, guild, false);
        }

        if (guild.Settings.AssignPrivateMeetingRole_OnVoiceChannelJoin)
        {
            SocketRole voiceMeetingTextRole = _user.Guild.Roles.First(x => x.Name == guild.Settings.PrivateMeetingRole);
            string privateMeetingVoiceChnlName = guild.Settings.PrivateMeetingVoiceChannelName;

            if (CurrentVoiceState.VoiceChannel != null && CurrentVoiceState.VoiceChannel.Name == privateMeetingVoiceChnlName)
            {
                await _user.RemoveRoleAsync(voiceMeetingTextRole);
                _logger.LogInformation($"Remove Role: {_user.Username} ({_user.Id}) in {_user.Guild.Name} -> User is no longer connected to {privateMeetingVoiceChnlName} Voice Channel; Removed the {voiceMeetingTextRole.Name} role.");
            }

            if (NewVoiceState.VoiceChannel != null && NewVoiceState.VoiceChannel.Name == privateMeetingVoiceChnlName)
            {
                await _user.AddRoleAsync(voiceMeetingTextRole);
                _logger.LogInformation($"Give Role: {_user.Username} ({_user.Id}) in {_user.Guild.Name} -> User Connected to {privateMeetingVoiceChnlName} Voice Channel; Added the {voiceMeetingTextRole.Name} role");
            }
        }
    }

    /// <summary>
    /// Handle user's joining a guild the bot is in.
    /// </summary>
    /// <param name="User">Socket Guild User from discord</param>
    /// <returns>None</returns>
    private async Task UserJoined(SocketGuildUser User)
    {
        _logger.LogInformation($"User Joined: {User.Username} ({User.Id}) joined {User.Guild.Name}");
        GuildModel guild = await _guildData.GetGuildAsync(User.Guild.Id);
        if (guild.Settings.AutoAssignRoles_OnJoin)
        {
            await GiveRole(User, guild);
        }
    }

    /// <summary>
    /// Give specific roles/permissions to users that have their discord account linked to VATUSA
    /// </summary>
    /// <param name="User">Socket Guild User from Discord</param>
    /// <param name="Guild">Guild Model from Database</param>
    /// <param name="SendDM_OnVatusaNotFound">Send a direct message to the user stating their account's are not linked.</param>
    /// <returns>Embed Builder showing New roles and nickname that was assigned to that user.</returns>
    public async Task<EmbedBuilder> GiveRole(SocketGuildUser User, GuildModel Guild, bool SendDM_OnVatusaNotFound = true)
    {
        EmbedBuilder embed = new()
        {
            Color = Color.Green,
            Title = "Your roles have been assigned"
        };

        VatusaUserData? userModel = await VatusaApi.GetVatusaUserInfo(User.Id);

        string guildName = User.Guild.Name;

        if (userModel == null && SendDM_OnVatusaNotFound)
        {
            SocketGuildChannel rolesChannel = User.Guild.Channels.First(x => x.Name == Guild.Settings.RolesTextChannelName);

            string linkInstructions =
                $"Hello, I am an automated program that is here to help you get your `{guildName}` Discord permissions/roles setup.\n\n" +
                "To do this, I need you to sync your Discord account with the VATUSA Discord server; You may do this by going to your VATUSA profile https://vatusa.net/my/profile > “VATUSA Discord Link”.\n\n" +
                $"When you are complete, join a voice channel or go to the <#{rolesChannel.Id}> channel in the `{guildName}` discord server and complete the `{Guild.Settings.Prefix}GR` command.\n\n" +
                "If you are unable to do this, please private message one of the Administrators of the discord.";

            await User.CreateDMChannelAsync().Result.SendMessageAsync(linkInstructions);
            _logger.LogInformation($"No Role: {User.Username} ({User.Id}) in {User.Guild.Name} -> Not found in VATUSA, no roles were assigned.");

            embed.Title = "Not Linked";
            embed.Description = "Your Discord account is not linked on VATUSA. Link it here: \nhttps://vatusa.net/my/profile";
            embed.Color = Color.Red;
            return embed;
        }

        if (userModel == null) return new EmbedBuilder() { Title = "Not Linked", Color = Color.Red, Description = "Your Discord account is not linked on VATUSA. Link it here: \nhttps://vatusa.net/my/profile" };

        SocketRole verifiedRole = User.Guild.Roles.First(x => x.Name == Guild.Settings.VerifiedRoleName);

        await User.AddRoleAsync(verifiedRole);
        _logger.LogInformation($"Give Role: {User.Username} ({User.Id}) in {User.Guild.Name} -> Found user in VATUSA; Assigned {verifiedRole?.Name} role to user.");
        embed.Description += $"{verifiedRole?.Mention} ";

        if (Guild.Settings.AssignArtccStaffRole && !string.IsNullOrEmpty(Guild.Settings.ArtccStaffRoleName))
        {
            if (HasArtccStaffRole(userModel))
            {
                SocketRole? artccStaffRole = User.Guild.Roles.First(x => x.Name == Guild.Settings.ArtccStaffRoleName);
                await User.AddRoleAsync(artccStaffRole);
                _logger.LogInformation($"Give Role: {User.Username} ({User.Id}) in {User.Guild.Name} -> Found user in VATUSA, user also is staff; Assigned {artccStaffRole?.Name} role to user.");
                embed.Description += artccStaffRole?.Mention + " ";
            }
        }

        if (Guild.Settings.AutoChangeNicknames)
        {
            var nickname = await ChangeNickname(User, userModel);

            embed.Footer = new EmbedFooterBuilder() { Text = "Your new nickname is: " + nickname };
        }
        return embed;
    }

    /// <summary>
    /// Change the Users Nickname. If the user has a "|" in their nickname only change it AFTER the pipe symbol
    /// </summary>
    /// <param name="User">Socket Guild User from discord.</param>
    /// <param name="UserData">User Model from VATUSA API</param>
    /// <returns>A string for what the user's nickname was changed to.</returns>
    private async Task<string> ChangeNickname(SocketGuildUser User, VatusaUserData? UserData)
    {
        string newNickname = $"{UserData?.data?.fname} {UserData?.data?.lname} | {UserData?.data?.facility}";

        if (User.Nickname != null && User.Nickname.Contains('|'))
        {
            newNickname = User.Nickname[..User.Nickname.IndexOf("|")] + newNickname[newNickname.IndexOf("|")..];
        }

        try
        {
            _logger.LogInformation($"Nickname: Changing {User.Username} ({User.Id}) nickname -> from {User.Nickname} to {newNickname}");
            await User.ModifyAsync(u => u.Nickname = newNickname);
            return newNickname;
        }
        catch (Exception ex)
        {
            if (ex.Message.Contains("Missing Permissions"))
            {
                _logger.LogWarning($"Missing Permissions: Could not change Nickname for {User.Username} ({User.Id}) in {User.Guild.Name}");
                return "I could not change your nickname.";
            }
            throw;
        }
    }

    /// <summary>
    /// Check for any staff roles in the User Model from VATUSA API
    /// </summary>
    /// <param name="userData">User Modle from VATUSA API</param>
    /// <returns>True if the user has a staff role, otherwise returns false.</returns>
    private static bool HasArtccStaffRole(VatusaUserData userData)
    {
        if (userData == null) return false;

        if (userData.data?.roles?.Length >= 1)
        {
            foreach (StaffRole role in userData.data.roles)
            {
                if (new string[] { "ATM", "DATM", "TA", "EC", "FE", "WM" }.Contains(role.role))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
