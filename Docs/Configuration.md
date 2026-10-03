# Configuration
The bot has two kinds of configuration:

1. **App settings**: how the bot runs (token, server ID, logging). Set in `appsettings.json` and overridden by secrets.
2. **Server settings**: what the bot does in Discord (which events are on, which roles and channels to use). Stored in a SQLite database and changed in Discord with [`/admin`](Bot%20Commands/Admin%20User%20Commands.md).

## Where settings come from
Later sources override earlier ones:

1. `appsettings.json`: non-secret defaults. Committed to the repository.
2. `appsettings.Development.json`: overrides for development (test server, debug logging). Only used when the environment is `Development`.
3. **User-secrets**: secrets on a development machine. Only used in `Development`.
4. **Environment variables**: secrets in production, normally from `bot.env`. A setting `Section:Key` becomes `Section__Key`, e.g. `Bot:Token` → `Bot__Token`.

**Secrets never go in `appsettings.json`.** The Docker image contains no secrets.

## App settings

### `Bot`
| Setting | Default | Description |
|---|---|---|
| `Token` | *(required)* | Bot token from the Discord Developer Portal. **Secret.** |
| `GuildId` | FE-Buddy's ID | The one server the bot serves. Development uses the test server's ID. |
| `Status` | *(empty)* | Status text shown under the bot's name. |
| `DisconnectWebhookUrl` | *(empty)* | Discord webhook that's notified when the bot restarts after losing its connection. **Secret.** |
| `DisconnectTimeoutSeconds` | `60` | How long the bot may stay disconnected from Discord before it exits so Docker restarts it. |
| `MessageContentIntent` | `false` | Read message text, so every reply in an issue post can be copied to GitHub. **Turn on "Message Content Intent" in the Developer Portal first** ([Discord Setup](Discord%20Setup.md#developer-portal)); if the portal doesn't allow it, Discord refuses the connection and the bot can't start. Development has it on. |
| `DataDirectory` | `data` | Folder for the SQLite database, relative to the app folder. |

The bot won't start without `Token` and `GuildId`. If Discord rejects the token, the bot stops immediately with `Discord rejected the bot token` in the log.

### `Heartbeat`
Sends a heartbeat to an [Uptime Kuma](https://github.com/louislam/uptime-kuma) push monitor while the bot is connected to Discord. If the bot disconnects, the heartbeats stop and Uptime Kuma reports it as down.

| Setting | Default | Description |
|---|---|---|
| `Enabled` | `false` | Turn the heartbeat on. |
| `Url` | *(empty)* | Push URL **without** the `?status=up…` part, e.g. `http://host:3001/api/push/abc123`. **Secret.** |
| `IntervalSeconds` | `60` | How often to send it. |

### `GitHub`
The GitHub App used for [issue reporting](GitHub%20Issue%20Sync.md). Issue features stay off until `AppId` and `PrivateKeyPath` are set. Setting up the app: [GitHub App Setup](GitHub%20App%20Setup.md).

| Setting | Default | Description |
|---|---|---|
| `AppId` | `0` | The GitHub App's App ID. |
| `ClientId` | FE-BUDDY Bot's | The GitHub App's Client ID (starts with `Iv`). Only needed for `/link-github`. Not a secret. |
| `PrivateKeyPath` | *(empty)* | Path to the app's private key (`.pem`), relative to the app folder. In Docker: `secrets/github-app.pem`. **The file is a secret.** |
| `Repository` | `Nikolai558/FE-BUDDY` | Where issues are created. Development uses `Nikolai558/FE-BUDDYBot-sandbox`. |
| `PollIntervalSeconds` | `120` | How often the bot checks GitHub for new issues. Checks where nothing changed don't count against GitHub's rate limit. |

### `GuildDefaults`
Only used the **first time** the bot starts with an empty database. It seeds the server settings, looking up the role and channel names given here. After that, change settings with `/admin`; these values aren't read again.

| Setting | FE-Buddy default |
|---|---|
| `AssignRolesOnJoin`, `AssignRolesOnVoiceJoin`, `PrivateMeetingRoleEnabled`, `ChangeNicknames`, `AssignStaffRole` | `true` |
| `VerifiedRoleName` | `Verified` |
| `StaffRoleName` | `ARTCC STAFF` |
| `PrivateMeetingRoleName` | `voice-meeting-txt` |
| `PrivateMeetingChannelName` | `Private Meeting` |
| `RolesChannelName` | `assign-my-roles` |

Names that can't be found are logged as a warning and left unset; set them with `/admin roles` or `/admin channels`.

### `Serilog`
Logging goes to the console and to `logs/fe-buddy-bot<date>.log`, one file per day, kept for 31 days. Production logs at `Information`; development logs at `Debug`.

## Secrets

### Production (`bot.env`)
Copy [`bot.env.example`](../FEBuddyDiscordBot/bot.env.example) to `bot.env` next to `docker-compose.yml` and fill it in. Keep it readable by root only (`chmod 600`).

```
Bot__Token=...
Bot__DisconnectWebhookUrl=...
Bot__Status=
Heartbeat__Enabled=false
Heartbeat__Url=...
GitHub__AppId=...
GitHub__PrivateKeyPath=secrets/github-app.pem
```

After changing `bot.env`, recreate the container (see [Deployment](Deployment.md#common-commands)).

### Development (user-secrets)
```
cd FEBuddyDiscordBot
dotnet user-secrets set "Bot:Token" "<development bot token>"
dotnet user-secrets set "GitHub:AppId" "<app id>"
dotnet user-secrets set "GitHub:PrivateKeyPath" "<full path to the .pem file>"
```

Use the **development** bot's token. Running the production token locally would start a second copy of the live bot.

## Server settings database
Server settings are stored in `data/febuddybot.db` (SQLite), one row per server. The same file also holds which forum post belongs to which GitHub issue, and each member's issue submissions. View and change them in Discord:

* `/admin settings`: show everything, with ⚠️ next to anything missing or deleted
* `/admin events`: turn automatic behaviors on or off
* `/admin roles`: choose the Verified, Staff and private meeting roles
* `/admin channels`: choose the private meeting voice channel and the roles channel
* `/admin issues`, `/admin dev-task-roles`: set up issue reporting (see [Discord Setup](Discord%20Setup.md#issue-reporting))

Roles and channels are stored by ID, so renaming them in Discord doesn't break anything.

**Backing up:** copy `data/febuddybot.db`.

**Starting over:** stop the bot, delete `data/febuddybot.db`, and start it again. It re-seeds from `GuildDefaults`.
