using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using FEBuddyDiscordBot.Models;

namespace FEBuddyDiscordBot.DataAccess;

public sealed class GitHubException(string message) : Exception(message);

public sealed record GitHubLabel([property: JsonPropertyName("name")] string Name);

public sealed record GitHubUser(
    [property: JsonPropertyName("login")] string Login,
    [property: JsonPropertyName("avatar_url")] string? AvatarUrl);

public sealed record GitHubIssue(
    [property: JsonPropertyName("number")] int Number,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("body")] string? Body,
    [property: JsonPropertyName("html_url")] string HtmlUrl,
    [property: JsonPropertyName("state")] string State,
    [property: JsonPropertyName("state_reason")] string? StateReason,
    [property: JsonPropertyName("labels")] IReadOnlyList<GitHubLabel> Labels,
    [property: JsonPropertyName("user")] GitHubUser? User,
    [property: JsonPropertyName("pull_request")] object? PullRequest,
    [property: JsonPropertyName("closed_by")] GitHubUser? ClosedBy = null)
{
    public bool IsPullRequest => PullRequest is not null;
    public bool IsOpen => State == "open";
    public IEnumerable<string> LabelNames => Labels.Select(l => l.Name);
}

public sealed record GitHubApp([property: JsonPropertyName("id")] long Id);

public sealed record GitHubComment(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("html_url")] string HtmlUrl,
    [property: JsonPropertyName("issue_url")] string IssueUrl,
    [property: JsonPropertyName("body")] string? Body,
    [property: JsonPropertyName("user")] GitHubUser? User,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
    [property: JsonPropertyName("updated_at")] DateTimeOffset UpdatedAt,
    [property: JsonPropertyName("performed_via_github_app")] GitHubApp? PerformedViaGitHubApp)
{
    /// <summary>The issue number, from the end of <see cref="IssueUrl"/>.</summary>
    public int IssueNumber => int.Parse(IssueUrl[(IssueUrl.LastIndexOf('/') + 1)..], System.Globalization.CultureInfo.InvariantCulture);
}

public sealed record GitHubRelease(
    [property: JsonPropertyName("tag_name")] string TagName,
    [property: JsonPropertyName("prerelease")] bool Prerelease,
    [property: JsonPropertyName("draft")] bool Draft);

/// <summary>
/// The few GitHub REST endpoints the bot uses, signed in as the GitHub App.
/// </summary>
public sealed class GitHubApi
{
    public const string HttpClientName = "github";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly GitHubAppAuth _auth;
    private readonly GitHubOptions _options;

    private string? _openIssuesETag;

    public GitHubApi(IHttpClientFactory httpClientFactory, GitHubAppAuth auth, IOptions<GitHubOptions> options)
    {
        _httpClientFactory = httpClientFactory;
        _auth = auth;
        _options = options.Value;
    }

    public bool IsConfigured => _options.IsConfigured;

    public string Repository => _options.Repository;

    /// <summary>The GitHub App's ID, to recognize comments the bot itself made.</summary>
    public long AppId => _options.AppId;

    public async Task<GitHubIssue> CreateIssueAsync(string title, string body, IEnumerable<string> labels, CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await SendAsync(HttpMethod.Post, $"repos/{Repository}/issues",
            JsonContent.Create(new { title, body, labels = labels.ToArray() }), cancellationToken: cancellationToken);
        return await ReadAsync<GitHubIssue>(response, "create an issue", cancellationToken);
    }

    public async Task UpdateIssueBodyAsync(int number, string body, CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await SendAsync(HttpMethod.Patch, $"repos/{Repository}/issues/{number}",
            JsonContent.Create(new { body }), cancellationToken: cancellationToken);
        await ReadAsync<GitHubIssue>(response, $"update issue #{number}", cancellationToken);
    }

    public async Task<GitHubComment> CreateCommentAsync(int issueNumber, string body, CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await SendAsync(HttpMethod.Post, $"repos/{Repository}/issues/{issueNumber}/comments",
            JsonContent.Create(new { body }), cancellationToken: cancellationToken);
        return await ReadAsync<GitHubComment>(response, $"comment on issue #{issueNumber}", cancellationToken);
    }

    /// <summary>Edit a comment. Returns false if it no longer exists (someone deleted it on GitHub).</summary>
    public async Task<bool> UpdateCommentAsync(long commentId, string body, CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await SendAsync(HttpMethod.Patch, $"repos/{Repository}/issues/comments/{commentId}",
            JsonContent.Create(new { body }), cancellationToken: cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound) return false;
        await ReadAsync<GitHubComment>(response, $"edit comment {commentId}", cancellationToken);
        return true;
    }

    /// <summary>Delete a comment. Already-deleted comments are fine.</summary>
    public async Task DeleteCommentAsync(long commentId, CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await SendAsync(HttpMethod.Delete, $"repos/{Repository}/issues/comments/{commentId}", cancellationToken: cancellationToken);
        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.NoContent) return;
        if (!response.IsSuccessStatusCode) throw new GitHubException($"GitHub refused to delete comment {commentId}: HTTP {(int)response.StatusCode}");
    }

    public async Task<GitHubIssue> GetIssueAsync(int number, CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await SendAsync(HttpMethod.Get, $"repos/{Repository}/issues/{number}", cancellationToken: cancellationToken);
        return await ReadAsync<GitHubIssue>(response, $"read issue #{number}", cancellationToken);
    }

    /// <summary>Issues (not pull requests) in the repository matching a GitHub search query, best match first.</summary>
    public async Task<IReadOnlyList<GitHubIssue>> SearchIssuesAsync(string terms, int count, CancellationToken cancellationToken = default)
    {
        string query = Uri.EscapeDataString($"{terms} repo:{Repository} is:issue");
        using HttpResponseMessage response = await SendAsync(HttpMethod.Get, $"search/issues?q={query}&per_page={count}", cancellationToken: cancellationToken);
        return (await ReadAsync<SearchResponse>(response, "search issues", cancellationToken)).Items;
    }

    /// <summary>
    /// Every open issue (pull requests excluded), newest first, or null when nothing has changed since the last call.
    /// "Not modified" answers don't count against GitHub's rate limit.
    /// </summary>
    public async Task<IReadOnlyList<GitHubIssue>?> ListOpenIssuesIfChangedAsync(CancellationToken cancellationToken = default)
    {
        List<GitHubIssue> issues = [];
        for (int page = 1; ; page++)
        {
            using HttpResponseMessage response = await SendAsync(HttpMethod.Get,
                $"repos/{Repository}/issues?state=open&sort=created&direction=desc&per_page=100&page={page}",
                etag: page == 1 ? _openIssuesETag : null, cancellationToken: cancellationToken);

            // New issues always land on page 1, so an unchanged page 1 means nothing new.
            if (page == 1 && response.StatusCode == HttpStatusCode.NotModified) return null;

            List<GitHubIssue> batch = await ReadAsync<List<GitHubIssue>>(response, "list open issues", cancellationToken);
            if (page == 1) _openIssuesETag = response.Headers.ETag?.ToString();

            issues.AddRange(batch.Where(i => !i.IsPullRequest));
            if (batch.Count < 100) return issues;
        }
    }

    /// <summary>Issues (open and closed, pull requests excluded) changed at or after <paramref name="since"/>, oldest change first.</summary>
    public async Task<List<GitHubIssue>> ListIssuesUpdatedSinceAsync(DateTimeOffset since, CancellationToken cancellationToken = default)
    {
        List<GitHubIssue> issues = await ListAllAsync<GitHubIssue>(
            $"repos/{Repository}/issues?state=all&sort=updated&direction=asc&since={Iso(since)}", "list changed issues", cancellationToken);
        return issues.Where(i => !i.IsPullRequest).ToList();
    }

    /// <summary>Issue comments created or edited at or after <paramref name="since"/>, oldest first.</summary>
    public Task<List<GitHubComment>> ListCommentsUpdatedSinceAsync(DateTimeOffset since, CancellationToken cancellationToken = default) =>
        ListAllAsync<GitHubComment>($"repos/{Repository}/issues/comments?sort=updated&direction=asc&since={Iso(since)}", "list changed comments", cancellationToken);

    private static string Iso(DateTimeOffset time) => Uri.EscapeDataString(time.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", System.Globalization.CultureInfo.InvariantCulture));

    private async Task<List<T>> ListAllAsync<T>(string url, string action, CancellationToken cancellationToken)
    {
        List<T> all = [];
        for (int page = 1; ; page++)
        {
            using HttpResponseMessage response = await SendAsync(HttpMethod.Get, $"{url}&per_page=100&page={page}", cancellationToken: cancellationToken);
            List<T> batch = await ReadAsync<List<T>>(response, action, cancellationToken);
            all.AddRange(batch);
            if (batch.Count < 100) return all;
        }
    }

    /// <summary>Make the next <see cref="ListOpenIssuesIfChangedAsync"/> return the full list even if nothing changed.</summary>
    public void ForgetOpenIssuesETag() => _openIssuesETag = null;

    /// <summary>FE-BUDDY's most recent published releases (pre-releases included), newest first.</summary>
    public async Task<IReadOnlyList<GitHubRelease>> ListReleasesAsync(int count, CancellationToken cancellationToken = default)
    {
        using HttpResponseMessage response = await SendAsync(HttpMethod.Get, $"repos/{_options.ReleasesRepository}/releases?per_page={count}", cancellationToken: cancellationToken);
        return (await ReadAsync<List<GitHubRelease>>(response, "list releases", cancellationToken)).Where(r => !r.Draft).ToList();
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, HttpContent? content = null, string? etag = null, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured) throw new GitHubException("The GitHub App isn't configured (GitHub:AppId and GitHub:PrivateKeyPath).");

        HttpClient http = _httpClientFactory.CreateClient(HttpClientName);
        using HttpRequestMessage request = new(method, url) { Content = content };
        if (etag is not null) request.Headers.IfNoneMatch.Add(EntityTagHeaderValue.Parse(etag));

        try
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", await _auth.GetTokenAsync(cancellationToken));
            return await http.SendAsync(request, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException || (ex is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            throw new GitHubException($"GitHub could not be reached: {ex.Message}");
        }
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, string action, CancellationToken cancellationToken)
    {
        if (!response.IsSuccessStatusCode)
        {
            string detail = await response.Content.ReadAsStringAsync(cancellationToken);
            if (detail.Length > 300) detail = detail[..300];
            throw new GitHubException($"GitHub refused to {action}: HTTP {(int)response.StatusCode} {detail}");
        }

        return (await response.Content.ReadFromJsonAsync<T>(cancellationToken))!;
    }

    private sealed record SearchResponse([property: JsonPropertyName("items")] List<GitHubIssue> Items);
}
