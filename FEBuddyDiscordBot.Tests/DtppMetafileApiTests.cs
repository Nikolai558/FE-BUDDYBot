using System.Net;
using FEBuddyDiscordBot.DataAccess;
using Microsoft.Extensions.Logging.Abstractions;

namespace FEBuddyDiscordBot.Tests;

public class DtppMetafileApiTests
{
    // The first bytes of the real file (with its byte order mark).
    private const string MetafileStart = "﻿<?xml version=\"1.0\" encoding=\"utf-8\" standalone=\"yes\"?>\r\n" +
                                         "<digital_tpp cycle=\"2611\" from_edate=\"0901Z  10/29/26\" to_edate=\"0901Z  11/26/26\">\r\n" +
                                         "  <state_code ID=\"AK\" state_fullname=\"ALASKA\">";

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
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private static (DtppMetafileApi Api, StubHandler Handler) Create(HttpStatusCode status, string body = "")
    {
        StubHandler handler = new(_ => new HttpResponseMessage(status) { Content = new StringContent(body) });
        return (new DtppMetafileApi(new StubFactory(handler), NullLogger<DtppMetafileApi>.Instance), handler);
    }

    [Fact]
    public async Task Not_found_means_not_published_yet()
    {
        (DtppMetafileApi api, _) = Create(HttpStatusCode.NotFound, "<!DOCTYPE html><html><title>404 - File or directory not found.</title></html>");

        Assert.Equal(MetafileStatus.NotYetPublished, await api.CheckAsync("2612"));
    }

    [Fact]
    public async Task The_metafile_for_the_cycle_means_available_and_only_its_start_is_requested()
    {
        (DtppMetafileApi api, StubHandler handler) = Create(HttpStatusCode.PartialContent, MetafileStart);

        Assert.Equal(MetafileStatus.Available, await api.CheckAsync("2611"));
        Assert.Equal("https://aeronav.faa.gov/d-tpp/2611/xml_data/d-tpp_Metafile.xml", handler.LastRequest!.RequestUri!.ToString());
        Assert.Equal("bytes=0-1023", handler.LastRequest.Headers.Range!.ToString());
    }

    [Fact]
    public async Task A_server_that_ignores_the_range_and_sends_everything_still_works()
    {
        (DtppMetafileApi api, _) = Create(HttpStatusCode.OK, MetafileStart + new string(' ', 100_000));

        Assert.Equal(MetafileStatus.Available, await api.CheckAsync("2611"));
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, "<!DOCTYPE html><html><body>Maintenance</body></html>")]
    [InlineData(HttpStatusCode.OK, MetafileStart)] // another cycle's file
    [InlineData(HttpStatusCode.InternalServerError, "")]
    [InlineData(HttpStatusCode.Forbidden, "")]
    public async Task Anything_else_is_unknown(HttpStatusCode status, string body)
    {
        (DtppMetafileApi api, _) = Create(status, body);

        Assert.Equal(MetafileStatus.Unknown, await api.CheckAsync("2612"));
    }

    [Fact]
    public async Task Network_errors_are_unknown()
    {
        StubHandler handler = new(_ => throw new HttpRequestException("connection refused"));
        DtppMetafileApi api = new(new StubFactory(handler), NullLogger<DtppMetafileApi>.Instance);

        Assert.Equal(MetafileStatus.Unknown, await api.CheckAsync("2611"));
    }

    [Theory]
    [InlineData(MetafileStart, "2611")]
    [InlineData("<digital_tpp cycle=\"2612\">", "2612")]
    [InlineData("<html><body>cycle=\"2611\"</body></html>", null)]
    [InlineData("", null)]
    public void Cycle_comes_from_the_root_element(string start, string? cycle)
    {
        Assert.Equal(cycle, DtppMetafileApi.CycleIn(start));
    }
}
