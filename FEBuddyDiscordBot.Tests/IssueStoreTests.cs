using FEBuddyDiscordBot.DataAccess;
using FEBuddyDiscordBot.Models;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace FEBuddyDiscordBot.Tests;

public sealed class IssueStoreTests : IDisposable
{
    private sealed class TestEnvironment(string root) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Test";
        public string ApplicationName { get; set; } = "Test";
        public string ContentRootPath { get; set; } = root;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private readonly string _root = Path.Combine(Path.GetTempPath(), "febuddybot-tests-" + Guid.NewGuid().ToString("N"));

    private async Task<IssueStore> CreateStoreAsync()
    {
        IssueStore store = new(new BotDatabase(Options.Create(new BotOptions()), new TestEnvironment(_root)));
        await store.InitializeAsync();
        return store;
    }

    [Fact]
    public async Task Posts_survive_a_restart()
    {
        IssueStore first = await CreateStoreAsync();
        await first.SavePostAsync(268, 1234567890123456789, forumId: 555, reporterId: 42);

        IssueStore second = await CreateStoreAsync();

        Assert.Equal(1234567890123456789UL, second.GetPostId(268));
        Assert.Null(second.GetPostId(269));
        Assert.True(second.HasPostIn(268, 555));
        Assert.False(second.HasPostIn(268, 556));
    }

    [Fact]
    public async Task Hourly_count_ignores_denied_and_old_submissions()
    {
        IssueStore store = await CreateStoreAsync();
        await store.AddSubmissionAsync(7, SubmissionStatus.Created, "{}", 1);
        await store.AddSubmissionAsync(7, SubmissionStatus.Pending, "{}", null);
        long denied = await store.AddSubmissionAsync(7, SubmissionStatus.Pending, "{}", null);
        await store.TryResolveAsync(denied, SubmissionStatus.Denied);
        await store.AddSubmissionAsync(8, SubmissionStatus.Created, "{}", 2);

        Assert.Equal(2, await store.CountSubmissionsSinceAsync(7, DateTimeOffset.UtcNow.AddHours(-1)));
        Assert.Equal(0, await store.CountSubmissionsSinceAsync(7, DateTimeOffset.UtcNow.AddMinutes(1)));
    }

    [Fact]
    public async Task A_submission_is_resolved_only_once()
    {
        IssueStore store = await CreateStoreAsync();
        long id = await store.AddSubmissionAsync(7, SubmissionStatus.Pending, """{"Title":"x"}""", null);
        await store.SetApprovalMessageAsync(id, 99);

        Assert.True(await store.TryResolveAsync(id, SubmissionStatus.Created));
        Assert.False(await store.TryResolveAsync(id, SubmissionStatus.Denied));

        Submission? saved = await store.GetSubmissionAsync(id);
        Assert.Equal(SubmissionStatus.Created, saved!.Status);
        Assert.Equal(99UL, saved.ApprovalMessageId);
        Assert.Equal("""{"Title":"x"}""", saved.DraftJson);

        await store.ReopenAsync(id);
        Assert.Equal(SubmissionStatus.Pending, (await store.GetSubmissionAsync(id))!.Status);
    }

    [Fact]
    public async Task Post_state_comments_and_cursor_survive_a_restart()
    {
        IssueStore first = await CreateStoreAsync();
        await first.SavePostAsync(3, 111, forumId: 555, reporterId: null);
        await first.SavePostStateAsync(3, new PostState("#3 Title", false, ["Bug", "Won't fix"], "ABC"));
        DateTimeOffset edited = new(2026, 9, 29, 6, 32, 34, TimeSpan.Zero);
        await first.SaveCommentAsync(9001, 3, 222, edited);
        await first.SetSyncCursorAsync(edited);

        IssueStore second = await CreateStoreAsync();

        PostState? state = await second.GetPostStateAsync(3);
        Assert.True(state!.Matches(new PostState("#3 Title", false, ["Bug", "Won't fix"], "ABC")));
        Assert.False(state.Matches(new PostState("#3 Title", true, ["Bug", "Won't fix"], "ABC")));
        Assert.False(state.Matches(new PostState("#3 Title", false, ["Bug"], "ABC")));
        Assert.Equal((222UL, edited), await second.GetCommentAsync(9001));
        Assert.Equal(edited, await second.GetSyncCursorAsync());
        Assert.NotNull(await second.GetOldestPostTimeAsync());
    }

    [Fact]
    public async Task Forgetting_a_post_forgets_its_state_and_comments()
    {
        IssueStore store = await CreateStoreAsync();
        Assert.Null(await store.GetSyncCursorAsync());
        Assert.Null(await store.GetOldestPostTimeAsync());

        await store.SavePostAsync(3, 111, forumId: 555, reporterId: null);
        await store.SavePostStateAsync(3, new PostState("#3", true, [], "x"));
        await store.SaveCommentAsync(9001, 3, 222, DateTimeOffset.UtcNow);

        await store.ForgetPostAsync(3);

        Assert.Null(store.GetPostId(3));
        Assert.Null(await store.GetPostStateAsync(3));
        Assert.Null(await store.GetCommentAsync(9001));
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
