# Discord Admin User Commands
Commands for changing the bot's settings in the server. All `/admin` commands reply privately. For `events`, `roles` and `channels`, every option is optional: only the options you fill in are changed, and the reply shows the updated settings.

**Permission Requirements**
* `Manage Server`, or the bot owner
* In FE-Buddy they can be used in **#feb-helper**, **#owner-chat** and **#admin-chat** (see [Discord Setup](../Discord%20Setup.md#command-permissions)).

## Show Settings
`/admin settings`

Shows the bot's current configuration. Anything not set, or set to a role or channel that has since been deleted, is marked with ⚠️.

## Events
`/admin events [assign-on-join] [assign-on-voice-join] [private-meeting-role] [change-nicknames] [assign-staff-role]`

Turns the bot's automatic behaviors on or off (True/False). See [Automatic Events](Discord%20Event%20Commands.md) for what each one does.

Example: `/admin events private-meeting-role:False`

## Roles
`/admin roles [verified] [staff] [private-meeting]`

Pick the roles the bot assigns from Discord's role picker:
* `verified`: members linked on VATUSA
* `staff`: ARTCC staff (ATM, DATM, TA, EC, FE, WM)
* `private-meeting`: given while someone is in the private meeting voice channel

The bot warns you if it can't assign a role you picked: if its own role is below that role, if it doesn't have Manage Roles, or if the role is managed by an integration.

Example: `/admin roles verified:@Verified staff:@ARTCC STAFF`

## Channels
`/admin channels [private-meeting] [roles-channel]`

* `private-meeting`: the voice channel that grants the private meeting role
* `roles-channel`: the channel new members are pointed to for `/give-role`

Roles and channels are saved by ID, so renaming them later doesn't break anything.

---
