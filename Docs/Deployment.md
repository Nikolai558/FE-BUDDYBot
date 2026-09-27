# Deployment
GitHub Actions builds, tests and publishes the bot's Docker image on every push to `main`:

* `ghcr.io/nikolai558/fe-buddybot:latest`: the newest build
* `ghcr.io/nikolai558/fe-buddybot:sha-<commit>`: every build, for rollbacks

The server never builds the bot; it just pulls the image. Pull requests run the tests and a test build but publish nothing.

## Server layout
```
/home/febuddybot/bot/
├── docker-compose.yml   copied from FEBuddyDiscordBot/docker-compose.yml
├── bot.env              secrets (root only: chmod 600)
├── data/                SQLite settings database (owned by UID 1654)
└── logs/                log files, kept for 31 days (owned by UID 1654)
```

The container runs as a non-root user (UID 1654), so `data/` and `logs/` must be owned by that UID.

## First-time setup
```
sudo mkdir -p /home/febuddybot/bot && cd /home/febuddybot/bot
# copy docker-compose.yml and bot.env.example from FEBuddyDiscordBot/ in the repository
sudo cp bot.env.example bot.env && sudo chmod 600 bot.env
sudo nano bot.env                      # fill in Bot__Token (see Configuration.md)
sudo mkdir -p data logs && sudo chown -R 1654:1654 data logs
sudo docker compose up -d
docker logs febuddybot --tail 30
```

A healthy start logs `Startup: registered slash commands in FE-Buddy` followed by `Gateway: Ready`. On the first start it also logs `Database: seeded settings…`, with a warning for any role or channel in `GuildDefaults` it couldn't find.

## Common commands
Run these from `/home/febuddybot/bot`.

    Update to the newest image:
        sudo docker compose pull
        sudo docker compose up -d

    Roll back to an older build:
        edit docker-compose.yml: change ":latest" to ":sha-xxxxxxx"
        (tags are listed on the package page on GitHub), then
        sudo docker compose up -d

    Apply changes to bot.env:
        sudo docker compose up -d --force-recreate

    Restart:
        sudo docker compose restart

    Stop / start:
        sudo docker compose down
        sudo docker compose up -d

    Recent logs:
        docker logs febuddybot --tail 100

    Follow logs live:
        docker logs -f febuddybot

## Current Linode: old docker-compose
The current server has the old standalone `docker-compose` 1.29, not `docker compose`. On it:

    Update:
        sudo docker-compose -p febuddybot pull
        sudo docker-compose -p febuddybot down
        sudo docker-compose -p febuddybot up -d

* **Always run `down` before `up -d`.** docker-compose 1.29 crashes with `KeyError: 'ContainerConfig'` when it tries to recreate a running container on Docker Engine 25+. If that happens, the old container is left stopped and renamed; `down` then `up -d` fixes it.
* **Always pass `-p febuddybot`.** Otherwise it names the project after the folder (`bot`) and tries to create a second container with the same name.

## Backups
The only data is `data/febuddybot.db` (the server settings). Copy it somewhere safe. To restore, stop the bot, put the file back, and start it again.

## Troubleshooting
| In the log | Cause | Fix |
|---|---|---|
| `Discord rejected the bot token` | Wrong or reset token | Update `Bot__Token` in `bot.env` and recreate the container |
| `Bot:Token is missing` | `bot.env` not found or empty | Check `bot.env` is next to `docker-compose.yml` |
| `the bot is not a member of the configured server` | Wrong `Bot:GuildId`, or the bot was removed | Re-invite the bot (see [Discord Setup](Discord%20Setup.md#inviting-the-bot)) |
| `Config: The Verified role is not set or no longer exists` | Role deleted or never set | `/admin roles` |
| `Missing Permissions: could not give …` | Bot's role is below that role | Move the bot's role up (see [Discord Setup](Discord%20Setup.md#bot-role)) |
| `VATUSA: lookup … failed` | VATUSA is down or slow | Nothing to do; members are skipped until it's back |
| `Watchdog: disconnected from Discord for over 60s` | Network or Discord outage | The bot exits and Docker restarts it automatically |
| `Gateway: Server requested a reconnect` | Routine | Nothing to do |

## Building locally
```
cd FEBuddyDiscordBot
docker build -t febuddybot:dev .
docker run --rm -e DOTNET_ENVIRONMENT=Development -e Bot__Token=<development token> febuddybot:dev
```
