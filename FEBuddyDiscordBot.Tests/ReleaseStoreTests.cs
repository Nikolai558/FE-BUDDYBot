using FEBuddyDiscordBot.Announcements;
using FEBuddyDiscordBot.DataAccess;
using FEBuddyDiscordBot.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace FEBuddyDiscordBot.Tests;

public sealed class ReleaseStoreTests : IDisposable
{
    private sealed class TestEnvironment(string root) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Test";
        public string ApplicationName { get; set; } = "Test";
        public string ContentRootPath { get; set; } = root;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private readonly string _root = Path.Combine(Path.GetTempPath(), "febuddybot-tests-" + Guid.NewGuid().ToString("N"));

    private async Task<ReleaseStore> CreateStoreAsync()
    {
        ReleaseStore store = new(new BotDatabase(Options.Create(new BotOptions()), new TestEnvironment(_root)));
        await store.InitializeAsync();
        return store;
    }

    [Fact]
    public async Task Seen_releases_and_the_started_marker_survive_a_restart()
    {
        ReleaseStore store = await CreateStoreAsync();
        Assert.Empty(await store.GetSeenTagsAsync());
        Assert.Null(await store.GetWatchStartedAsync());

        DateTimeOffset before = DateTimeOffset.UtcNow.AddSeconds(-1);
        await store.RecordSkippedAsync("2.9.3", ReleaseLevel.Stable);
        await store.RecordSkippedAsync(ReleaseStore.StartedMarker, ReleaseLevel.Stable);
        Assert.InRange((await store.GetWatchStartedAsync())!.Value, before, DateTimeOffset.UtcNow.AddSeconds(1));
        await store.RecordPendingAsync("3.0.0-beta.10", ReleaseLevel.Beta, "https://example.com/r", 10, 20);

        ReleaseStore restarted = await CreateStoreAsync();
        Assert.Equal(new HashSet<string> { "2.9.3", ReleaseStore.StartedMarker, "3.0.0-beta.10" }, await restarted.GetSeenTagsAsync());

        ReleaseRecord? pending = await restarted.GetAsync("3.0.0-beta.10");
        Assert.Equal(new ReleaseRecord("3.0.0-beta.10", ReleaseLevel.Beta, ReleaseStatus.Pending, "https://example.com/r", 10, 20, null, null), pending);
        Assert.Null(await restarted.GetAsync("1.0.0"));
    }

    [Fact]
    public async Task Only_one_admin_can_handle_a_preview()
    {
        ReleaseStore store = await CreateStoreAsync();
        await store.RecordPendingAsync("3.0.0", ReleaseLevel.Stable, "https://example.com/r", 10, 20);

        Assert.True(await store.TryMoveAsync("3.0.0", ReleaseStatus.Pending, ReleaseStatus.Posting, byUserId: 1));
        Assert.False(await store.TryMoveAsync("3.0.0", ReleaseStatus.Pending, ReleaseStatus.Cancelled, byUserId: 2));

        await store.MarkPostedAsync("3.0.0", 30, 40);
        ReleaseRecord? posted = await store.GetAsync("3.0.0");
        Assert.Equal(ReleaseStatus.Posted, posted?.Status);
        Assert.Equal((30UL, 40UL), (posted?.PublicChannelId, posted?.PublicMessageId));
    }

    [Fact]
    public async Task A_failed_post_goes_back_to_pending()
    {
        ReleaseStore store = await CreateStoreAsync();
        await store.RecordPendingAsync("3.0.0", ReleaseLevel.Stable, "https://example.com/r", 10, 20);

        Assert.True(await store.TryMoveAsync("3.0.0", ReleaseStatus.Pending, ReleaseStatus.Posting, byUserId: 1));
        Assert.True(await store.TryMoveAsync("3.0.0", ReleaseStatus.Posting, ReleaseStatus.Pending));

        Assert.Equal(ReleaseStatus.Pending, (await store.GetAsync("3.0.0"))?.Status);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }
}
