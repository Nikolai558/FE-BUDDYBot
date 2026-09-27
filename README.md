# FE-BUDDYBot
Discord bot for the FE-Buddy Discord server. It links members to their [VATUSA](https://www.vatusa.net/) accounts: it assigns roles, sets nicknames, and manages the private meeting role.

## [Bot Commands](https://github.com/Nikolai558/FE-BUDDYBot/tree/releases/Docs)

## Development
Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download).

Development runs against the test server (`appsettings.Development.json`) with the development bot. Store its token once in user-secrets:

```
cd FEBuddyDiscordBot
dotnet user-secrets set "Bot:Token" "<development bot token>"
dotnet run --environment Development
```

Run the tests with `dotnet test` from the repository root.

## Configuration
* `appsettings.json`: non-secret settings. Committed.
* Secrets (`Bot:Token`, `Bot:DisconnectWebhookUrl`, `Heartbeat:Url`) come from user-secrets in development, or environment variables in production (`Bot__Token`, ...). See [`bot.env.example`](FEBuddyDiscordBot/bot.env.example).
* Server settings (which events are on, which roles and channels to use) live in a SQLite database in `data/` and are changed in Discord with `/admin`. On first run the database is seeded from the `GuildDefaults` section.

## Running with Docker
```
cd FEBuddyDiscordBot
cp bot.env.example bot.env   # fill in the token
mkdir -p data logs && sudo chown -R 1654:1654 data logs
docker compose up -d
```
