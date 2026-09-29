# Discord Setup
What the bot needs in the Discord Developer Portal and in the server. There are two Discord applications: **production** (runs on the server in FE-Buddy) and **development** (runs locally in the test server). Set both up the same way.

## Developer Portal
[discord.com/developers/applications](https://discord.com/developers/applications) → your app → **Bot**:

| Setting | Value | Why |
|---|---|---|
| **Server Members Intent** | **On** | Required. The bot needs member join events and the member list. |
| **Message Content Intent** | Off | Not used. The bot only uses slash commands. |
| **Presence Intent** | Off | Not used. |

**Token:** click **Reset Token** to get a new one. Discord only shows a token once. The production token goes in `bot.env` on the server and the development token in user-secrets (see [Configuration](Configuration.md#secrets)). Resetting a token disconnects anything still using the old one.

## Inviting the bot
Invite link, with `<APPLICATION_ID>` from the portal's **General Information** page:

```
https://discord.com/oauth2/authorize?client_id=<APPLICATION_ID>&scope=bot%20applications.commands&permissions=402654208
```

`402654208` gives the bot **View Channels**, **Manage Roles** and **Manage Nicknames**, which is all it needs.

## Bot role
The bot's role (**Buddy Helper** in FE-Buddy) needs:

* **View Channels**
* **Manage Roles**: to give and remove the Verified, ARTCC Staff and private meeting roles
* **Manage Nicknames**: to set nicknames

It does **not** need Administrator; leave that off. The bot also doesn't need to see any particular channel. Slash commands work in channels the bot can't see, and it still gets voice join events for private voice channels.

**Role order matters.** In **Server Settings → Roles**, the bot's role must be **above** every role it assigns (Verified, ARTCC Staff, private meeting role). `/admin roles` warns you if it isn't.

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

## Issue reporting
Members report FE-BUDDY issues from Discord. Set up the [GitHub App](GitHub%20App%20Setup.md) first. How it works is described in [GitHub Issue Sync](GitHub%20Issue%20Sync.md).

All the bot's permissions below are **channel permissions** (Edit Channel → Permissions). The bot's role needs nothing new server-wide.

### 1. Forum channel
Create a **Forum** channel, e.g. `#fe-buddy-issues`. Forums need **Community** turned on (Server Settings → Enable Community).

| Role | Allow | Deny |
|---|---|---|
| @everyone | View Channel, Read Message History | **Create Posts**, **Send Messages in Posts** |
| Verified | Send Messages in Posts | |
| Buddy Helper (the bot) | View Channel, Create Posts, Send Messages in Posts, Manage Posts, **Manage Channel**, Embed Links, Attach Files, Read Message History | |

Only the bot creates posts, one per GitHub issue. It adds the forum's tags itself (that's what **Manage Channel** is for) and only it applies them, because GitHub's labels decide them.

### 2. Submit channel
A text channel for the report buttons, e.g. `#submit-an-issue`.

| Role | Allow | Deny |
|---|---|---|
| @everyone | View Channel, Read Message History, Use Application Commands | Send Messages |
| Buddy Helper (the bot) | View Channel, Send Messages, Embed Links | |

### 3. Approval channel
A member's second submission within an hour waits for approval in a private channel. FE-Buddy uses **#admin-chat**. The bot needs View Channel, Send Messages, Embed Links, Attach Files and Read Message History there.

To ping **@Admin**, either make the Admin role mentionable (Server Settings → Roles → Admin → "Allow anyone to @mention this role"), or give the bot **Mention @everyone, @here, and All Roles** in #admin-chat only. `/admin issues` warns you if it can't ping the role.

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
