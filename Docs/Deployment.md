# Deployment
GitHub Actions builds, tests and publishes the bot's Docker image on every push to `main`:

* `ghcr.io/nikolai558/fe-buddybot:latest`: the newest build
* `ghcr.io/nikolai558/fe-buddybot:sha-<commit>`: every build, for rollbacks

The server never builds the bot; it just pulls the image. Pull requests run the tests and a test build but publish nothing.

## Server layout
```
/opt/stacks/febuddybot/      root:docker 750
├── docker-compose.yml       copied from FEBuddyDiscordBot/docker-compose.yml (root:docker 640)
├── bot.env                  secrets (root:docker 640)
├── data/                    SQLite settings database (UID 1654 only, 700)
├── logs/                    log files, kept for 31 days (UID 1654 only, 700)
└── secrets/                 GitHub App private key (UID 1654 only, 500; key file 400)
```

The container runs as a non-root user (UID 1654, the `febuddybot` user on the server), so `data/`, `logs/` and `secrets/` must be owned by that UID. Members of the `docker` group can run `docker compose` here without sudo, but can't read the data folders; use sudo for that.

The container's filesystem is read-only, it runs with no extra privileges, and it is capped at 512 MB of memory and 200 processes (see `docker-compose.yml`). If it hits the memory cap, only the bot is killed and Docker restarts it. It joins the shared `monitoring` network so its heartbeat reaches Uptime Kuma at `http://uptime-kuma:3001`.

## First-time setup
```
sudo install -d -m 750 -o root -g docker /opt/stacks/febuddybot && cd /opt/stacks/febuddybot
# copy docker-compose.yml and bot.env.example from FEBuddyDiscordBot/ in the repository
sudo cp bot.env.example bot.env && sudo chown root:docker bot.env && sudo chmod 640 bot.env
sudo nano bot.env                      # fill in Bot__Token (see Configuration.md)
sudo install -d -m 700 -o 1654 data logs && sudo install -d -m 500 -o 1654 secrets
docker network inspect monitoring >/dev/null 2>&1 || docker network create monitoring
docker compose up -d
docker logs febuddybot --tail 30
```

A healthy start logs `Startup: FE-Buddy bot 1.0.0 (abc1234) is online` (the commit the image was built from), `Startup: registered slash commands in FE-Buddy` and `Gateway: Ready`. The two `Startup:` lines also show in the bot log channel, if one is set. On the first start it also logs `Database: seeded settings…`, with a warning for any role or channel in `GuildDefaults` it couldn't find.

## Common commands
Run these from `/opt/stacks/febuddybot` as a member of the `docker` group.

    Update to the newest image:
        docker compose pull
        docker compose up -d

    Roll back to an older build:
        edit docker-compose.yml: change ":latest" to ":sha-xxxxxxx"
        (tags are listed on the package page on GitHub), then
        docker compose up -d

    Apply changes to bot.env:
        docker compose up -d --force-recreate

    Restart:
        docker compose restart

    Stop / start:
        docker compose down
        docker compose up -d

    Recent logs:
        docker logs febuddybot --tail 100

    Follow logs live:
        docker logs -f febuddybot

The server reboots itself at 01:00 (America/Los_Angeles) when a security update needs it. `restart: unless-stopped` brings the bot back afterwards, and Uptime Kuma alerts if the heartbeat stops.

## Backups
The only data is `data/febuddybot.db` (the server settings). Copy it somewhere safe (`sudo` is needed to read it). To restore, stop the bot, put the file back owned by UID 1654, and start it again.

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
