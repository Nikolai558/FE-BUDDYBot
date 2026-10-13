# Discord Setup
What the bot needs in the Discord Developer Portal and in the server. There are two Discord applications: **production** (runs on the server in FE-Buddy) and **development** (runs locally in the test server). Set both up the same way.

## Developer Portal
[discord.com/developers/applications](https://discord.com/developers/applications) → your app → **Bot**:

| Setting | Value | Why |
|---|---|---|
| **Server Members Intent** | **On** | Required. The bot needs member join events and the member list. |
| **Message Content Intent** | On, if issue replies are copied to GitHub | Lets the bot read replies in issue posts. Turn it on here **before** setting `Bot:MessageContentIntent` to `true`, or the bot can't connect. Leave it off otherwise. |
| **Presence Intent** | Off | Not used. |

**Token:** click **Reset Token** to get a new one. Discord only shows a token once. The production token goes in `bot.env` on the server and the development token in user-secrets (see [Configuration](Configuration.md#secrets)). Resetting a token disconnects anything still using the old one.

## Inviting the bot
Invite link, with `<APPLICATION_ID>` from the portal's **General Information** page:

```
https://discord.com/oauth2/authorize?client_id=<APPLICATION_ID>&scope=bot%20applications.commands&permissions=292460686416
```

`292460686416` is the permission set in [Bot role](#bot-role) below. Discord gives it to the bot's own role (the one named after the bot) when it joins.

## Bot role
All of the bot's permissions go on its own role, **server-wide**, so it can do its job in every channel. In FE-Buddy that role is **Buddy Helper**.

| Permission | Used for |
|---|---|
| View Channels, Read Message History | Seeing every channel; reading messages for `/clear-messages` and issue replies |
| Send Messages, Send Messages in Threads, Embed Links, Attach Files | AIRAC pings, issue posts and replies, approval requests, the issue panel, bot logs |
| Add Reactions | The warning reaction on issue replies that couldn't be sent to GitHub |
| Mention @everyone, @here, and All Roles | Pinging the AIRAC pings, approver and alert roles, which aren't mentionable |
| Manage Messages | `/clear-messages` |
| Manage Threads | Archiving, locking and editing issue posts |
| Manage Channels | Adding the issue forum's tags |
| Manage Roles | Giving and removing the Verified, ARTCC Staff, private meeting and AIRAC pings roles |
| Manage Nicknames | Setting nicknames |

It does **not** need Administrator; leave that off.

**The bot should see every channel.** Where a category denies @everyone **View Channel**, allow it to the bot's role on that category. From November 16, 2026, Discord hides channels a bot can't view from it entirely. The same goes for any permission a channel denies @everyone, such as **Create Posts** on the issue forum: allow it to the bot's role there.

**Role order matters.** In **Server Settings → Roles**, the bot's **highest** role must be **above** every role it assigns (Verified, ARTCC Staff, private meeting role, AIRAC pings role). `/admin roles` warns you if it isn't. In FE-Buddy that's the **Bot** role: it has no permissions, gives the bot its name color, and sits at the top of the list so the bot can manage every role below it.

FE-Buddy's roles, channels and permissions are managed with [discord-iac](../tools/discord-iac/README.md) from a `desired.yaml` kept locally (it's git-ignored). Change them there rather than by hand, so the file stays the record. The tool uses the bot's token, and a bot can only grant permissions it has, so for changes that grant anything beyond the list above, turn on Administrator for the **Bot** role while you run `apply`, then turn it off.

Discord never lets bots change the **server owner's** nickname; the bot skips the owner on purpose.

## Command permissions
Two layers decide who can use each command:

1. **The bot's own check** (built in): the permission each command needs, shown in the [command docs](README.md#commands). The bot enforces this no matter what's set in Discord.
2. **Discord's command settings**: **Server Settings → Integrations → the bot**. These control who can see each command and **which channels** it can be used in. The server owner bypasses these settings.

Discord checks a command's own settings first, then falls back to the bot-wide settings.

### FE-Buddy's current setup
**Bot-wide channels**

| Channel | |
|---|---|
| All Channels | ✕ |
| #feb-helper | ✓ |
| #owner-chat | ✓ |
| #admin-chat | ✓ |

**Command overrides**

| Command | Override |
|---|---|
| `/give-role` | All Channels ✕, #assign-my-roles ✓. So `/give-role` works **only** in #assign-my-roles. |
| `/admin` | @everyone ✕, Admin ✓ |

Result: `/give-role` works only in #assign-my-roles, and `/admin`, `/staff` and `/owner` only in #feb-helper, #owner-chat and #admin-chat.

**#assign-my-roles** must let @everyone **View Channel**, **Send Messages** and **Use Application Commands**, so new, unverified members can run `/give-role` there.

Discord apps sometimes keep old command permissions cached for a few minutes; restarting Discord refreshes them.

## AIRAC pings
The bot pings a role when the next AIRAC cycle's d-TPP MetaFile is out (see [Automatic Events](Bot%20Commands/Discord%20Event%20Commands.md#next-airac-metafile-ping)).

1. **Role:** create **AIRAC Notifications**, below the bot's role. The bot can ping it through its **Mention @everyone, @here, and All Roles** permission, so it doesn't need to be mentionable.
2. **Channel:** create **#next-airac-notifications** where everyone can read it but members can't post. In FE-Buddy it's in the **Info** category, which only allows @everyone **View Channel**; nobody but Admin and the bot gets **Send Messages** there, so it needs no overwrites of its own.
3. **Tell the bot**, in #admin-chat:
   ```
   /admin roles airac-pings:@AIRAC Notifications
   /admin channels airac-pings:#next-airac-notifications
   /admin airac-check times:1500z, 2100z
   ```
   The first two reply with a warning if the bot can't post there or can't ping the role. `/admin airac-check` is optional: `1500z, 2100z` is the default.
4. **Allow `/airac-pings`** under **Server Settings → Integrations → the bot**, e.g. in **#assign-my-roles** and **#next-airac-notifications**. The **Stop these pings** button works anywhere.

Verified members get the role the next time the bot checks their roles (`/give-role` or connecting to voice). To give it to everyone now, run `/staff check-users mode:Add roles` (run **Report only** first to preview).

## Release announcements
New FE-BUDDY releases wait as a preview for an admin to post (see [Automatic Events](Bot%20Commands/Discord%20Event%20Commands.md#fe-buddy-release-announcements)). This needs the [GitHub App](GitHub%20App%20Setup.md).

1. **Role:** create **Release Notifications**, below the bot's role. Members opt in and out with the button under each announcement, so the bot must be able to assign it. Like the AIRAC role, it doesn't need to be mentionable.
2. **Channels:** a public channel for the announcements (read-only for members, like #next-airac-notifications), and a private review channel. **#admin-chat** works.
3. **Emoji:** the title ends with the server's `:FEB_cheer:` emoji, if it has one with exactly that name.
4. **Tell the bot**, in #admin-chat:
   ```
   /admin releases level:Beta (beta, RC and stable) channel:#announcements review-channel:#admin-chat ping-role:@Release Notifications
   ```
   It warns you about anything missing.

The first check after this only notes the releases already out. Only releases after that are announced.

## Bot log channel
The bot posts what it does, and any errors, in a private channel, and pings a role when something goes wrong (see [Admin Commands](Bot%20Commands/Admin%20User%20Commands.md#bot-logs)).

1. **Role:** create a role for the people who should be pinged, e.g. **Bot Alerts**, and give it to them. It doesn't need any permissions or to be mentionable. A separate role lets someone keep seeing the channel without the pings.
2. **Channel:** create **#bot-logs** in the private **Discord Admins** category, so only Admin and the bot can see it. Members shouldn't post there.
3. **Tell the bot**, in #admin-chat:
   ```
   /admin logs channel:#bot-logs alert-role:@Bot Alerts
   ```
   It warns you if it can't post or ping there, or if everyone can see the channel.

## Issue reporting
Members report FE-BUDDY issues from Discord. Set up the [GitHub App](GitHub%20App%20Setup.md) first. How it works is described in [GitHub Issue Sync](GitHub%20Issue%20Sync.md).

The bot's server-wide permissions ([Bot role](#bot-role)) cover everything it does here. The channels only need overwrites for who else may post. In FE-Buddy both channels are in the **FE-BUDDY Issues** category, which only Verified members can see.

### 1. Forum channel
Create a **Forum** channel, e.g. `#fe-buddy-issues`. Forums need **Community** turned on (Server Settings → Enable Community).

| Role | Allow | Deny |
|---|---|---|
| @everyone | | **Create Posts** |
| Verified | Send Messages in Posts | |
| Buddy Helper (the bot) | Create Posts | |

Only the bot creates posts, one per GitHub issue, so **Create Posts** is denied to @everyone and allowed back to the bot. It adds the forum's tags itself (with **Manage Channels**) and only it applies them, because GitHub's labels decide them.

### 2. Submit channel
A text channel for the report buttons, e.g. `#submit-an-issue`. Same overwrites as the forum, plus **Verified: Send Messages**, so members can type `/report` there.

### 3. Approval channel
A member's second submission within an hour waits for approval in a private channel. FE-Buddy uses **#admin-chat**, in the private **Discord Admins** category, where the bot's role is allowed **View Channel**. The approval message pings the approver role through the bot's **Mention @everyone, @here, and All Roles** permission. `/admin issues` warns you if it can't post or ping there.

### 4. Tell the bot
In #admin-chat:
```
/admin issues forum:#fe-buddy-issues submit-channel:#submit-an-issue approval-channel:#admin-chat approver-role:@Admin
/admin dev-task-roles        → pick Contributor, Project Management and Admin
/admin issue-panel
```
`/admin issues` lists anything still missing. Within two minutes the bot makes a post for every **open** FE-BUDDY issue. Closed issues aren't copied.

### 5. Allow /report
The report buttons work anywhere they're posted. The `/report` command follows the command permissions: under **Server Settings → Integrations → the bot → /report**, allow **#submit-an-issue**. The bot itself checks for the **Verified** role.

### 6. Replies to GitHub
By default, every reply in an issue post is copied to the GitHub issue as a comment. Edits and deletes follow it. This needs the **Message Content Intent** turned on in the Developer Portal and `Bot:MessageContentIntent` set to `true` (see [Configuration](Configuration.md#bot)).

To copy only chosen messages instead, use `/admin issues replies:Only "Send to GitHub"`. Members then right-click their message → **Apps → Send to GitHub**. Staff (approvers and Manage Server) can send anyone's message.

The **Send to GitHub** action follows the command permissions like the slash commands. Under **Server Settings → Integrations → the bot → Send to GitHub**, allow the issue forum.

### 7. Linking GitHub accounts
`/link-github` lets members link their GitHub account, so issues and replies they send are credited to it. It needs the GitHub App's **Client ID** in `GitHub:ClientId` and **Device Flow** turned on in the app ([GitHub App Setup](GitHub%20App%20Setup.md)). Allow `/link-github` and `/unlink-github` wherever members should use them, e.g. **#submit-an-issue**.
