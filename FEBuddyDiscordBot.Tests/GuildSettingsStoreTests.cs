using FEBuddyDiscordBot.DataAccess;
using FEBuddyDiscordBot.Models;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace FEBuddyDiscordBot.Tests;

public sealed class GuildSettingsStoreTests : IDisposable
{
    private sealed class TestEnvironment(string root) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Test";
        public string ApplicationName { get; set; } = "Test";
        public string ContentRootPath { get; set; } = root;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private readonly string _root = Path.Combine(Path.GetTempPath(), "febuddybot-tests-" + Guid.NewGuid().ToString("N"));

    private GuildSettingsStore CreateStore(ulong guildId = 962547562341072896)
    {
        IOptions<BotOptions> options = Options.Create(new BotOptions { GuildId = guildId });
        return new(new BotDatabase(options, new TestEnvironment(_root)), options, NullLogger<GuildSettingsStore>.Instance);
    }

    [Fact]
    public async Task Empty_database_returns_disabled_settings()
    {
        GuildSettingsStore store = CreateStore();
        await store.InitializeAsync();

        Assert.False(store.IsSeeded);
        Assert.Equal(GuildSettings.Disabled, store.Current);
    }

    [Fact]
    public async Task Updates_survive_a_restart()
    {
        GuildSettingsStore first = CreateStore();
        await first.InitializeAsync();
        await first.UpdateAsync(s =>
        {
            s.AssignRolesOnJoin = true;
            s.VerifiedRoleId = 1234567890123456789;
        });

        GuildSettingsStore second = CreateStore();
        await second.InitializeAsync();

        Assert.True(second.IsSeeded);
        Assert.True(second.Current.AssignRolesOnJoin);
        Assert.Equal(1234567890123456789UL, second.Current.VerifiedRoleId);
    }

    [Fact]
    public async Task Update_does_not_mutate_previous_snapshot()
    {
        GuildSettingsStore store = CreateStore();
        await store.InitializeAsync();
        GuildSettings before = await store.UpdateAsync(s => s.ChangeNicknames = false);

        await store.UpdateAsync(s => s.ChangeNicknames = true);

        Assert.False(before.ChangeNicknames);
        Assert.True(store.Current.ChangeNicknames);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }
}
