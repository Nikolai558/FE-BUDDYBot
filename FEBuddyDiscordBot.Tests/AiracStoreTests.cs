using FEBuddyDiscordBot.DataAccess;
using FEBuddyDiscordBot.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace FEBuddyDiscordBot.Tests;

public sealed class AiracStoreTests : IDisposable
{
    private sealed class TestEnvironment(string root) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Test";
        public string ApplicationName { get; set; } = "Test";
        public string ContentRootPath { get; set; } = root;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private readonly string _root = Path.Combine(Path.GetTempPath(), "febuddybot-tests-" + Guid.NewGuid().ToString("N"));

    private async Task<AiracStore> CreateStoreAsync()
    {
        IOptions<BotOptions> options = Options.Create(new BotOptions { GuildId = 1 });
        AiracStore store = new(new BotDatabase(options, new TestEnvironment(_root)));
        await store.InitializeAsync();
        return store;
    }

    [Fact]
    public async Task Opting_out_and_back_in_survives_a_restart()
    {
        AiracStore store = await CreateStoreAsync();
        await store.SetOptedOutAsync(353684697651478528, optedOut: true);
        await store.SetOptedOutAsync(353684697651478528, optedOut: true); // pressing the button twice is fine
        await store.SetOptedOutAsync(42, optedOut: true);
        await store.SetOptedOutAsync(42, optedOut: false);

        AiracStore restarted = await CreateStoreAsync();

        Assert.True(restarted.IsOptedOut(353684697651478528));
        Assert.False(restarted.IsOptedOut(42));
        Assert.False(restarted.IsOptedOut(7));
    }

    [Fact]
    public async Task Each_cycle_is_announced_once()
    {
        AiracStore store = await CreateStoreAsync();
        Assert.False(await store.WasAnnouncedAsync("2611"));

        await store.RecordAnnouncementAsync("2611", DateTimeOffset.UtcNow, 10, 20);
        await store.RecordAnnouncementAsync("2611", DateTimeOffset.UtcNow, 10, 21);

        AiracStore restarted = await CreateStoreAsync();
        Assert.True(await restarted.WasAnnouncedAsync("2611"));
        Assert.False(await restarted.WasAnnouncedAsync("2612"));
    }

    [Fact]
    public async Task Announcement_time_is_read_back_in_utc()
    {
        AiracStore store = await CreateStoreAsync();
        DateTimeOffset found = new(2026, 10, 12, 15, 0, 3, TimeSpan.Zero);
        await store.RecordAnnouncementAsync("2611", found, 10, 20);

        Assert.Equal(found, await store.GetAnnouncedAtAsync("2611"));
        Assert.Null(await store.GetAnnouncedAtAsync("2612"));
    }

    [Fact]
    public async Task The_latest_announcement_is_the_newest_cycle()
    {
        AiracStore store = await CreateStoreAsync();
        Assert.Null(await store.LatestAnnouncementAsync());

        await store.RecordAnnouncementAsync("2612", DateTimeOffset.UtcNow, 10, 22);
        await store.RecordAnnouncementAsync("2611", DateTimeOffset.UtcNow, 10, 21);
        await store.RecordAnnouncementAsync("2701", DateTimeOffset.UtcNow, 11, 23);
        await store.RecordAnnouncementAsync("2613", DateTimeOffset.UtcNow, 10, 24);

        AiracStore restarted = await CreateStoreAsync();
        Assert.Equal(("2701", 11UL, 23UL), await restarted.LatestAnnouncementAsync());
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
