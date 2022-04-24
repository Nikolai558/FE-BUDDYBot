using Discord.Interactions;

namespace FEBuddyDiscordBot.Modals;

public class ConfigModal_ChannelNames : IModal
{
    public string Title => "Discord Channel Name Server Configuration";

    [InputLabel("Private Meeting Voice Channel Name")]
    [ModalTextInput("PrivateMeetingVoiceChannelName", TextInputStyle.Short, maxLength: 50)]
    [RequiredInput(false)]
    public string PrivateMeetingVoiceChannelName { get; set; }

    [InputLabel("Roles Text Channel Name")]
    [ModalTextInput("RolesTextChannelName", TextInputStyle.Short, maxLength: 50)]
    [RequiredInput(false)]
    public string RolesTextChannelName { get; set; }
}
