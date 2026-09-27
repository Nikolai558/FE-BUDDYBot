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
