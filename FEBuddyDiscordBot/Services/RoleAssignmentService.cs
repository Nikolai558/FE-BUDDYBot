using FEBuddyDiscordBot.DataAccess;
using static FEBuddyDiscordBot.Models.VatusaUserModel;

namespace FEBuddyDiscordBot.Services;
public class RoleAssignmentService
{
    private readonly IServiceProvider _services;
    private readonly IConfigurationRoot _config;
    private readonly DiscordSocketClient _discord;
    private readonly ILogger _logger;
    private readonly VatusaApi _vatusaApi;

    public RoleAssignmentService(IServiceProvider services)
    {
        _services = services;
        _config = _services.GetRequiredService<IConfigurationRoot>();
        _discord = _services.GetRequiredService<DiscordSocketClient>();
        _logger = _services.GetRequiredService<ILogger<RoleAssignmentService>>();
        _vatusaApi = _services.GetRequiredService<VatusaApi>();

        _discord.UserJoined += UserJoined;
        _discord.UserVoiceStateUpdated += UserConnectedToVoice;

        _logger.LogInformation("Loaded: RoleAssignmentService");
    }

    private async Task UserConnectedToVoice(SocketUser User, SocketVoiceState CurrentVoiceState, SocketVoiceState NewVoiceState)
    {
        SocketGuildUser _user = (SocketGuildUser)User;

        if (_user == null) return;

        if (NewVoiceState.VoiceChannel != null) await GiveRole(_user, false);

        SocketRole voiceMeetingTextRole = _user.Guild.Roles.First(x => x.Name == "voice-meeting-txt");
        string privateMeetingVoiceChnlName = "Private Meeting";

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

    private async Task UserJoined(SocketGuildUser User)
    {
        _logger.LogInformation($"User Joined: {User.Username} ({User.Id}) joined {User.Guild.Name}");
        await GiveRole(User);
    }

    public async Task GiveRole(SocketGuildUser User, bool SendDM_OnVatusaNotFound = true)
    {
        VatusaUserData? userModel = await _vatusaApi.GetVatusaUserInfo(User.Id);
        
        string guildName = User.Guild.Name;
        SocketRole artccStaffRole = User.Guild.Roles.First(x => x.Name.ToUpper() == "ARTCC STAFF");
        SocketRole verifiedRole = User.Guild.Roles.First(x => x.Name.ToUpper() == "VERIFIED");
        SocketGuildChannel rolesChannel = User.Guild.Channels.First(x => x.Name == "assign-my-roles");

        if (userModel == null && SendDM_OnVatusaNotFound)
        {
            string linkInstructions = 
                $"Hello, I am an automated program that is here to help you get your {guildName} Discord permissions/roles setup.\n\n" +
                "To do this, I need you to sync your Discord account with the VATUSA Discord server; You may do this by going to your VATUSA profile https://vatusa.net/my/profile > “VATUSA Discord Link”.\n\n" +
                $"When you are complete, join a voice channel or go to the FE-Buddy Discord <#{rolesChannel.Id}> channel and complete the `!GR` command.\n\n" +
                "If you are unable to do this, please private message one of the Administrators of the discord.";

            await User.CreateDMChannelAsync().Result.SendMessageAsync(linkInstructions);
            _logger.LogInformation($"No Role: {User.Username} ({User.Id}) in {User.Guild.Name} -> Not found in VATUSA, no roles were assigned.");
            return;
        }

        if (userModel == null) return;

        await User.AddRoleAsync(verifiedRole);
        _logger.LogInformation($"Give Role: {User.Username} ({User.Id}) in {User.Guild.Name} -> Found user in VATUSA; Assigned {verifiedRole?.Name} role to user.");

        if (hasArtccStaffRole(userModel))
        {
            await User.AddRoleAsync(artccStaffRole);
            _logger.LogInformation($"Give Role: {User.Username} ({User.Id}) in {User.Guild.Name} -> Found user in VATUSA, user also is staff; Assigned {artccStaffRole?.Name} role to user.");
        }

        await ChangeNickname(User, userModel);
    }

    private async Task ChangeNickname(SocketGuildUser User, VatusaUserData UserData)
    {
        string newNickname = $"{UserData.data.fname} {UserData.data.lname} | {UserData.data.facility}";

        if (User.Nickname.Contains('|'))
        {
            newNickname = User.Nickname[..User.Nickname.IndexOf("|")] + newNickname[newNickname.IndexOf("|")..];
        }

        try
        {
            await User.ModifyAsync(u => u.Nickname = newNickname);
        }
        catch (Exception ex)
        {
            if (ex.Message.Contains("Missing Permissions"))
            {
                _logger.LogWarning($"Missing Permissions: Could not change Nickname for {User.Username} ({User.Id}) in {guildName}");
                return;
            }
            throw;
        }
    }

    private bool hasArtccStaffRole(VatusaUserData userData)
    {
        if(userData == null) return false;

        if (userData.data?.roles?.Length >= 1)
        {
            return true;
        }

        return false;
    }
}
