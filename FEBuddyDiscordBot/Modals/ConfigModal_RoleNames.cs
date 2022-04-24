using Discord.Interactions;

namespace FEBuddyDiscordBot.Modals;

public class ConfigModal_RoleNames : IModal
{
    public string Title => "Discord Role Name Server Configuration";

    [InputLabel("Private Meeting Role Name")]
    [ModalTextInput("PrivateMeetingRole", TextInputStyle.Short, maxLength: 50)]
    [RequiredInput(false)]
    public string PrivateMeetingRole { get; set; }

    [InputLabel("Verified Role Name")]
    [ModalTextInput("VerifiedRoleName", TextInputStyle.Short, maxLength: 50)]
    [RequiredInput(false)]
    public string VerifiedRoleName { get; set; }

    [InputLabel("ARTCC Staff Role Name")]
    [ModalTextInput("ArtccStaffRoleName", TextInputStyle.Short, maxLength: 50)]
    [RequiredInput(false)]
    public string ArtccStaffRoleName { get; set; }
}
