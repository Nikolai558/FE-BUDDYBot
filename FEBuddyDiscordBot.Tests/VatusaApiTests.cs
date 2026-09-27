using System.Net;
using FEBuddyDiscordBot.DataAccess;
using Microsoft.Extensions.Logging.Abstractions;

namespace FEBuddyDiscordBot.Tests;

public class VatusaApiTests
{
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(respond(request));
        }
    }

    private sealed class StubFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false) { BaseAddress = new Uri("https://api.vatusa.net/") };
    }

    private static (VatusaApi Api, StubHandler Handler) Create(HttpStatusCode status, string body = "")
    {
        StubHandler handler = new(_ => new HttpResponseMessage(status) { Content = new StringContent(body) });
        return (new VatusaApi(new StubFactory(handler), NullLogger<VatusaApi>.Instance), handler);
    }

    [Fact]
    public async Task Not_found_means_not_linked()
    {
        (VatusaApi api, _) = Create(HttpStatusCode.NotFound);

        VatusaLookup result = await api.GetUserByDiscordIdAsync(123);

        Assert.Equal(VatusaLookupStatus.NotLinked, result.Status);
    }

    [Fact]
    public async Task Server_error_means_unavailable()
    {
        (VatusaApi api, _) = Create(HttpStatusCode.BadGateway);

        VatusaLookup result = await api.GetUserByDiscordIdAsync(123);

        Assert.Equal(VatusaLookupStatus.Unavailable, result.Status);
    }

    [Fact]
    public async Task Invalid_json_means_unavailable()
    {
        (VatusaApi api, _) = Create(HttpStatusCode.OK, "<html>maintenance</html>");

        VatusaLookup result = await api.GetUserByDiscordIdAsync(123);

        Assert.Equal(VatusaLookupStatus.Unavailable, result.Status);
    }

    [Fact]
    public async Task Found_user_is_parsed_and_uses_discord_lookup_url()
    {
        const string json = """
            {"data":{"cid":1187148,"fname":"Kyle","lname":"Sanders","facility":"ZOB","rating_short":"C3",
             "flag_nameprivacy":false,"discord_id":231176957335699456,
             "roles":[{"id":1,"cid":1187148,"facility":"ZOB","role":"FE"}]},"testing":false}
            """;
        (VatusaApi api, StubHandler handler) = Create(HttpStatusCode.OK, json);

        VatusaLookup result = await api.GetUserByDiscordIdAsync(231176957335699456);

        Assert.Equal(VatusaLookupStatus.Found, result.Status);
        Assert.Equal("Kyle", result.User!.FirstName);
        Assert.Equal("FE", result.User.Roles![0].Role);
        Assert.Equal("https://api.vatusa.net/v2/user/231176957335699456?d", handler.LastRequest!.RequestUri!.ToString());
    }
}
