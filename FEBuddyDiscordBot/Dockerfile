FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY FEBuddyDiscordBot.csproj ./
RUN dotnet restore

COPY . ./
# The commit being built (GitHub Actions passes it). The bot shows it in its "online" log line.
ARG SOURCE_REVISION=""
RUN dotnet publish -c Release -o /app --no-restore -p:SourceRevisionId=$SOURCE_REVISION

FROM mcr.microsoft.com/dotnet/runtime:10.0
WORKDIR /app
COPY --from=build /app .

# Secrets are passed in at runtime (see bot.env.example), never baked into the image.
ENV DOTNET_ENVIRONMENT=Production

# /app/data holds the SQLite database, /app/logs the log files. Mount both as volumes.
RUN mkdir -p /app/data /app/logs && chown -R $APP_UID /app/data /app/logs
USER $APP_UID

ENTRYPOINT ["dotnet", "FEBuddyDiscordBot.dll"]
