using FEBuddyDiscordBot.Models;
using Microsoft.Data.Sqlite;

namespace FEBuddyDiscordBot.DataAccess;

/// <summary>
/// The bot's SQLite database file (data/febuddybot.db). Each store creates its own tables.
/// </summary>
public sealed class BotDatabase
{
    private readonly string _connectionString;

    public BotDatabase(IOptions<BotOptions> options, IHostEnvironment environment)
    {
        string dataDirectory = Path.Combine(environment.ContentRootPath, options.Value.DataDirectory);
        Directory.CreateDirectory(dataDirectory);

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(dataDirectory, "febuddybot.db"),
            Pooling = false,
        }.ToString();
    }

    public async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken = default)
    {
        SqliteConnection connection = new(_connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }
}
