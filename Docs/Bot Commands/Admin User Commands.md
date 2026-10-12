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
`/admin roles [verified] [staff] [private-meeting] [airac-pings]`

Pick the roles the bot assigns from Discord's role picker:
* `verified`: members linked on VATUSA
* `staff`: ARTCC staff (ATM, DATM, TA, EC, FE, WM)
* `private-meeting`: given while someone is in the private meeting voice channel
* `airac-pings`: pinged when the next cycle's d-TPP MetaFile is out. Verified members get it unless they turn it off with `/airac-pings`.

The bot warns you if it can't assign a role you picked: if its own role is below that role, if it doesn't have Manage Roles, or if the role is managed by an integration.

Example: `/admin roles verified:@Verified staff:@ARTCC STAFF`

## Channels
`/admin channels [private-meeting] [roles-channel] [airac-pings]`

* `private-meeting`: the voice channel that grants the private meeting role
* `roles-channel`: the channel new members are pointed to for `/give-role`
* `airac-pings`: where the next-AIRAC-MetaFile pings are posted

For the AIRAC pings, `/admin roles` and `/admin channels` warn you if the bot can't post in the channel or can't ping the role (see [Discord Setup](../Discord%20Setup.md#airac-pings)).

Roles and channels are saved by ID, so renaming them later doesn't break anything.

## AIRAC Check
`/admin airac-check times:<times>`

Sets when the bot checks for the next AIRAC cycle's d-TPP MetaFile (see [Automatic Events](Discord%20Event%20Commands.md#next-airac-metafile-ping)). Times are **UTC**, every day. List as many as you like (up to 48), separated by commas or spaces. They replace the current list.
* `1200z, 2300z`: check at 12:00 and 23:00 UTC
* `1200z`: check once a day at 12:00 UTC
* `off`: stop checking

`1200`, `12:00z` and `12:00` also work. Until changed, the bot checks at `1500z, 2100z` (11:00 and 17:00 US Eastern daylight time). A new list applies straight away, without a restart. `/admin settings` shows the times and when the next check is.

Example: `/admin airac-check times:1200z, 2300z`

## Issues
`/admin issues [forum] [submit-channel] [approval-channel] [approver-role] [per-hour] [replies]`

Sets up [issue reporting](../GitHub%20Issue%20Sync.md). Only the options you fill in are changed.
* `forum`: the forum channel with one post per GitHub issue. When you set it, the bot adds the forum's tags and, within two minutes, a post for every open issue.
* `submit-channel`: where `/admin issue-panel` posts the report buttons
* `approval-channel`: private channel where a member's extra submissions wait for approval
* `approver-role`: pinged for approvals. Members with this role, or with Manage Server, can approve and deny.
* `per-hour`: how many issues a member may submit per hour before the rest need approval (default 1)
* `replies`: **Every reply** (default) copies every reply in an issue post to GitHub; **Only "Send to GitHub"** copies only messages sent with that message action. Every reply needs the Message Content intent (see [Discord Setup](../Discord%20Setup.md#6-replies-to-github)).

The reply lists anything still missing, including permissions the bot needs in those channels. Channel setup: [Discord Setup](../Discord%20Setup.md#issue-reporting).

Example: `/admin issues forum:#fe-buddy-issues submit-channel:#submit-an-issue approval-channel:#admin-chat approver-role:@Admin`

## Dev-Task Roles
`/admin dev-task-roles`

Shows a role picker. The roles you pick can submit **Development tasks**. Everyone with the Verified role can submit the other kinds. Members with Manage Server always can.

## Issue Panel
`/admin issue-panel`

Posts the "Report an FE-BUDDY issue" message with one button per issue type in the submit channel. Run it again after changing the panel's text; delete the old copy yourself.

---
