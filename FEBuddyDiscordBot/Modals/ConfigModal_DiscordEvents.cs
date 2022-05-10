using Discord.Interactions;

namespace FEBuddyDiscordBot.Modals;
public class ConfigModal_DiscordEvents : IModal
{
    public string Title => "Discord Event Server Configuration";

    [InputLabel("Assign Roles on Join")]
    [ModalTextInput("AutoAssignRoles_OnJoin", TextInputStyle.Short, placeholder: "True or False", minLength: 4, maxLength: 5)]
    public string AutoAssignRoles_OnJoin { get; set; }

    [InputLabel("Assign Roles on VC Connect")]
    [ModalTextInput("AutoAssignRoles_OnVoiceChannelJoin", TextInputStyle.Short, placeholder: "True or False", minLength: 4, maxLength: 5)]
    public string AutoAssignRoles_OnVoiceChannelJoin { get; set; }

    [InputLabel("Assign Private Meeting Role")]
    [ModalTextInput("AssignPrivateMeetingRole_OnVoiceChannelJoin", TextInputStyle.Short, placeholder: "True or False", minLength: 4, maxLength: 5)]
    public string AssignPrivateMeetingRole_OnVoiceChannelJoin { get; set; }

    [InputLabel("Auto Change Nicknames")]
    [ModalTextInput("AutoChangeNicknames", TextInputStyle.Short, placeholder: "True or False", minLength: 4, maxLength: 5)]
    public string AutoChangeNicknames { get; set; }

    [InputLabel("Assign ARTCC Staff Role")]
    [ModalTextInput("AssignArtccStaffRole", TextInputStyle.Short, placeholder: "True or False", minLength: 4, maxLength: 5)]
    public string AssignArtccStaffRole { get; set; }
}
