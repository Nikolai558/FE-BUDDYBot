# Deploying with Docker
GitHub Actions builds the bot's image on every push to `main` and publishes it as `ghcr.io/nikolai558/fe-buddybot` with two tags: `latest`, and `sha-<commit>` for rollbacks. The server never builds the bot; it just pulls the image.

## Server layout
```
/home/febuddybot/bot/
├── docker-compose.yml   copied from FEBuddyDiscordBot/docker-compose.yml
├── bot.env              secrets (root only: chmod 600)
├── data/                SQLite database (owned by UID 1654)
└── logs/                log files, kept for 31 days (owned by UID 1654)
```

## Common commands
Run these from `/home/febuddybot/bot`. On older servers, use `docker-compose` instead of `docker compose`.

    Update to the newest image:
        docker compose pull && docker compose up -d

    Roll back to an older build:
        edit docker-compose.yml: change ":latest" to ":sha-xxxxxxx", then
        docker compose up -d

    Restart:
        docker compose restart

    Stop / start:
        docker compose down
        docker compose up -d

    Recent logs:
        docker logs febuddybot --tail 100

    Follow logs live:
        docker logs -f febuddybot

## Building locally
    cd FEBuddyDiscordBot
    docker build -t febuddybot:dev .
    docker run --rm -e Bot__Token=<dev token> -e DOTNET_ENVIRONMENT=Development febuddybot:dev
