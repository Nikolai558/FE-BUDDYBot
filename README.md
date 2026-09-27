# FE-BUDDYBot
Discord bot for the FE-Buddy Discord server. It links members to their [VATUSA](https://www.vatusa.net/) accounts, gives them the right roles, and keeps their nicknames up to date.

## What it does
* **Verified role** for members whose Discord account is linked on VATUSA.
* **ARTCC Staff role** for members VATUSA lists as ATM, DATM, TA, EC, FE or WM.
* **Nicknames** in the form `First Last | ARTCC`. Anything before an existing `|` is kept, and members with VATUSA name privacy get their CID instead of their name.
* **Automatic updates** when someone joins the server or connects to voice. New members who aren't linked get a direct message explaining how to link their account.
* **Server-wide checks** for staff: preview, add, remove or fully update everyone's roles and nicknames.
* **Private meeting role**, optionally given while someone is in a chosen voice channel.

## Commands
| Command | Who | What it does |
|---|---|---|
| `/give-role` | Everyone | Get your roles and nickname |
| `/staff check-users` | Staff | Check every member against VATUSA; report or fix roles and nicknames |
| `/admin settings` / `events` / `roles` / `channels` | Admins | View and change the bot's settings |
| `/owner set-status` | Bot owner | Set the bot's status text |

Full details are in the **[documentation](Docs/README.md)**.

## Tech
* .NET 10 (LTS) worker app using [Discord.Net](https://github.com/discord-net/Discord.Net) 3.20
* SQLite for the server's settings; no external database
* VATUSA API v2 for member lookups
* Docker image published to `ghcr.io/nikolai558/fe-buddybot` by GitHub Actions on every push to `main`

## Quick start (development)
Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download).

Development runs against the test server (`appsettings.Development.json`) with the development bot. Store its token once in user-secrets:

```
cd FEBuddyDiscordBot
dotnet user-secrets set "Bot:Token" "<development bot token>"
dotnet run --environment Development
```

Run the tests from the repository root with `dotnet test`.

Never run the production token on your own computer while the server is running the bot: two copies would both react to every event.

## Repository layout
```
FEBuddyDiscordBot/          The bot
├── DataAccess/             VATUSA API client, SQLite settings store
├── Models/                 Configuration options and data models
├── Modules/SlashCommands/  /give-role, /staff, /admin, /owner
├── Services/               Startup, role assignment, logging, heartbeat, connection watchdog
├── appsettings.json        Non-secret settings (committed)
├── bot.env.example         Template for production secrets
├── Dockerfile
└── docker-compose.yml      Used on the server
FEBuddyDiscordBot.Tests/    Unit tests
Docs/                       Documentation
```

## Documentation
* [Commands and automatic events](Docs/README.md#commands)
* [Configuration](Docs/Configuration.md): settings, secrets and the settings database
* [Discord setup](Docs/Discord%20Setup.md): Developer Portal, bot permissions and command permissions
* [Deployment](Docs/Deployment.md): running, updating and rolling back on the server
