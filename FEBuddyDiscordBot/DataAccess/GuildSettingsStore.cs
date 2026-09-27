using System.Globalization;
using System.Text.Json;
using FEBuddyDiscordBot.Models;
using Microsoft.Data.Sqlite;

namespace FEBuddyDiscordBot.DataAccess;

/// <summary>
/// Stores the server's <see cref="GuildSettings"/> in a small SQLite database and keeps an in-memory copy.
/// </summary>
public sealed class GuildSettingsStore
{
    private readonly string _connectionString;
    private readonly ulong _guildId;
    private readonly ILogger<GuildSettingsStore> _logger;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    private GuildSettings? _cached;

    public GuildSettingsStore(IOptions<BotOptions> options, IHostEnvironment environment, ILogger<GuildSettingsStore> logger)
    {
        _logger = logger;
        _guildId = options.Value.GuildId;

        string dataDirectory = Path.Combine(environment.ContentRootPath, options.Value.DataDirectory);
        Directory.CreateDirectory(dataDirectory);

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(dataDirectory, "febuddybot.db"),
            Pooling = false,
        }.ToString();
    }

    /// <summary>True once settings exist in the database for this server.</summary>
    public bool IsSeeded => _cached is not null;

    /// <summary>
    /// Create the table if needed and load the saved settings into memory.
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await OpenAsync(cancellationToken);

        await using (SqliteCommand create = connection.CreateCommand())
        {
            create.CommandText = """
                CREATE TABLE IF NOT EXISTS guild_settings (
                    guild_id    INTEGER PRIMARY KEY,
                    json        TEXT NOT NULL,
                    updated_utc TEXT NOT NULL
                );
                """;
            await create.ExecuteNonQueryAsync(cancellationToken);
        }

        await using SqliteCommand select = connection.CreateCommand();
        select.CommandText = "SELECT json FROM guild_settings WHERE guild_id = $guildId";
        select.Parameters.AddWithValue("$guildId", (long)_guildId);

        if (await select.ExecuteScalarAsync(cancellationToken) is string json)
        {
            _cached = JsonSerializer.Deserialize<GuildSettings>(json);
            _logger.LogInformation("Database: loaded settings for guild {GuildId}", _guildId);
        }
        else
        {
            _logger.LogInformation("Database: no settings saved yet for guild {GuildId}", _guildId);
        }
    }

    /// <summary>
    /// Current settings. Returns <see cref="GuildSettings.Disabled"/> until the database has been seeded.
    /// Treat the returned object as read-only; use <see cref="UpdateAsync"/> to change settings.
    /// </summary>
    public GuildSettings Current => _cached ?? GuildSettings.Disabled;

    /// <summary>
    /// Apply a change to the settings and save it.
    /// </summary>
    public async Task<GuildSettings> UpdateAsync(Action<GuildSettings> change, CancellationToken cancellationToken = default)
    {
        await _writeLock.WaitAsync(cancellationToken);
        try
        {
            GuildSettings updated = Current with { };
            change(updated);
            await SaveAsync(updated, cancellationToken);
            _cached = updated;
            return updated;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task SaveAsync(GuildSettings settings, CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        await using SqliteCommand upsert = connection.CreateCommand();
        upsert.CommandText = """
            INSERT INTO guild_settings (guild_id, json, updated_utc) VALUES ($guildId, $json, $updated)
            ON CONFLICT(guild_id) DO UPDATE SET json = excluded.json, updated_utc = excluded.updated_utc;
            """;
        upsert.Parameters.AddWithValue("$guildId", (long)_guildId);
        upsert.Parameters.AddWithValue("$json", JsonSerializer.Serialize(settings));
        upsert.Parameters.AddWithValue("$updated", DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture));
        await upsert.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        SqliteConnection connection = new(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }
}
