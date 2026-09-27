# Discord Admin User Commands
All `/admin` commands reply privately. For `events`, `roles` and `channels`, every option is optional: only the options you fill in are changed.

**Permission Requirements**
* `Manage Server`, or the bot owner

## Show Settings
`/admin settings`

Shows the bot's current configuration. Anything missing or deleted is marked with ⚠️.

## Events
`/admin events [assign-on-join] [assign-on-voice-join] [private-meeting-role] [change-nicknames] [assign-staff-role]`

Turns the bot's automatic behaviors on or off (True/False).

## Roles
`/admin roles [verified] [staff] [private-meeting]`

Pick the roles the bot assigns from Discord's role picker. The bot warns you if it can't assign a role, for example because its own role is below that role.

## Channels
`/admin channels [private-meeting] [roles-channel]`

* `private-meeting`: the voice channel that grants the private meeting role
* `roles-channel`: the channel new members are pointed to for `/give-role`

---
