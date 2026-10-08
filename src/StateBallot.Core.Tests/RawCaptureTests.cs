using System.Net;
using System.Text;
using StateBallot.Core.Raw;

namespace StateBallot.Core.Tests;

public sealed class RawCaptureTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "raw-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task Sink_WritesPayloadAndLogEntry_WithMatchingHash()
    {
        var sink = NewSink("1");
        using var fetcher = new HttpFetcher(new FakeHandler(_ => Ok("<p>hi</p>", "text/html; charset=utf-8"))) { RawSink = sink };

        var body = await fetcher.GetStringAsync("https://sos.example.gov/elections/list");

        Assert.Equal("<p>hi</p>", body);
        var entry = Assert.Single(sink.Entries);
        Assert.Equal(1, entry.Seq);
        Assert.Equal("GET", entry.Method);
        Assert.Equal(200, entry.Status);
        Assert.Equal("text/html; charset=utf-8", entry.ContentType);
        Assert.Equal("001-sos-example-gov-elections-list.html", entry.FileName);
        var saved = File.ReadAllBytes(Path.Combine(sink.Directory, entry.FileName!));
        Assert.Equal(RawSink.Sha256(saved), entry.PayloadSha256);
        Assert.Equal(saved.LongLength, entry.Bytes);

        var log = RawSink.ReadLog(sink.Directory);
        Assert.Equal("WV", log.State);
        Assert.Equal(2026, log.Year);
        Assert.Equal(entry.PayloadSha256, Assert.Single(log.Fetches).PayloadSha256);
    }

    [Fact]
    public async Task Sink_GivesARepeatedUrlItsOwnSequenceNumber()
    {
        var sink = NewSink("1");
        var n = 0;
        using var fetcher = new HttpFetcher(new FakeHandler(_ => Ok($"page {++n}", "text/plain"))) { RawSink = sink };

        await fetcher.GetStringAsync("https://sos.example.gov/a");
        await fetcher.GetStringAsync("https://sos.example.gov/a");

        Assert.Equal([1, 2], sink.Entries.Select(e => e.Seq));
        Assert.NotEqual(sink.Entries[0].FileName, sink.Entries[1].FileName);
        Assert.NotEqual(sink.Entries[0].PayloadSha256, sink.Entries[1].PayloadSha256);
    }

    [Fact]
    public async Task Sink_RecordsA4xxWithNoPayload()
    {
        var sink = NewSink("1");
        using var fetcher = new HttpFetcher(new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound))) { RawSink = sink };

        Assert.Null(await fetcher.TryGetBytesAsync("https://sos.example.gov/not-yet.pdf"));

        var entry = Assert.Single(sink.Entries);
        Assert.Equal(404, entry.Status);
        Assert.Null(entry.FileName);
        Assert.Null(entry.PayloadSha256);
        Assert.Single(Directory.GetFiles(sink.Directory));
    }

    [Fact]
    public async Task Sink_RecordsAFetchThatFailedEveryRetry()
    {
        var sink = NewSink("1");
        using var fetcher = new HttpFetcher(new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.BadGateway))) { RawSink = sink };

        await Assert.ThrowsAsync<InvalidOperationException>(() => fetcher.GetStringAsync("https://sos.example.gov/down"));

        var entry = Assert.Single(sink.Entries);
        Assert.Equal(502, entry.Status);
        Assert.NotNull(entry.Error);
        Assert.Null(entry.FileName);
    }

    [Fact]
    public async Task Sink_RecordsTheTagOnEachFetch()
    {
        var sink = NewSink("1");
        using var fetcher = new HttpFetcher(new FakeHandler(_ => Ok("{}", "application/json"))) { RawSink = sink };

        await fetcher.GetStringAsync("https://sos.example.gov/guide", FetchTag.Of("county-guide", ("election", "9"), ("county", "01")));
        await fetcher.GetStringAsync("https://sos.example.gov/untagged");

        var log = RawSink.ReadLog(sink.Directory);
        Assert.Equal("county-guide", log.Fetches[0].Role);
        Assert.Equal(new SortedDictionary<string, string> { ["county"] = "01", ["election"] = "9" }, log.Fetches[0].Keys);
        Assert.Null(log.Fetches[1].Role);
        Assert.Null(log.Fetches[1].Keys);
    }

    [Fact]
    public async Task Reader_ServesCapturedPayloadsByRoleAndKeys()
    {
        var sink = NewSink("7");
        var pdf = new byte[] { 0x25, 0x50, 0x44, 0x46, 0x00, 0xff };
        using (var live = new HttpFetcher(new FakeHandler(req => req.RequestUri!.AbsolutePath switch
        {
            "/api" => Ok($"{{\"page\":{Encoding.UTF8.GetString(req.Content!.ReadAsByteArrayAsync().Result)}}}", "application/json"),
            "/doc.pdf" => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(pdf) },
            _ => Ok("café", "text/html; charset=iso-8859-1"),
        })) { RawSink = sink })
        {
            await live.GetStringAsync("https://sos.example.gov/", FetchTag.Of("home"));
            await live.PostJsonAsync("https://sos.example.gov/api", new { Page = 0 }, FetchTag.Of("page", ("page", "0")));
            await live.PostJsonAsync("https://sos.example.gov/api", new { Page = 1 }, FetchTag.Of("page", ("page", "1")));
            await live.GetBytesAsync("https://sos.example.gov/doc.pdf", FetchTag.Of("list", ("election", "9")));
        }

        var capture = new CaptureReader(sink.Directory);

        Assert.Equal(2026, capture.Year);
        Assert.Equal(DateOnly.FromDateTime(DateTime.UtcNow), capture.AsOf);
        Assert.Equal("café", capture.Require("home").Text());
        Assert.Equal(["{\"page\":{\"page\":0}}", "{\"page\":{\"page\":1}}"], capture.All("page").Select(p => p.Text()));
        Assert.Equal("1", capture.Require("page", ("page", "1")).Key("page"));
        Assert.Equal(pdf, capture.Require("list", ("election", "9")).Bytes());
        Assert.Null(capture.Find("list", ("election", "10")));
    }

    [Fact]
    public void Reader_MissingFetch_ThrowsNamingTheRoleAndKeys()
    {
        var sink = NewSink("7");
        sink.Record("GET", "https://sos.example.gov/a", null, 200, "text/plain", [1], 5, null, FetchTag.Of("guide", ("election", "1")));

        var ex = Assert.Throws<InvalidOperationException>(() => new CaptureReader(sink.Directory).Require("guide", ("election", "2")));
        Assert.Contains("'guide' (election=2)", ex.Message);
    }

    [Fact]
    public void Reader_TwoFetchesForOneLookup_Throws()
    {
        var sink = NewSink("7");
        sink.Record("GET", "https://sos.example.gov/a", null, 200, "text/plain", [1], 5, null, FetchTag.Of("guide", ("election", "1")));
        sink.Record("GET", "https://sos.example.gov/a", null, 200, "text/plain", [2], 5, null, FetchTag.Of("guide", ("election", "1")));

        Assert.Throws<InvalidOperationException>(() => new CaptureReader(sink.Directory).Find("guide", ("election", "1")));
    }

    [Fact]
    public async Task Reader_ShowsA4xxAsAFetchWithNoPayload()
    {
        var sink = NewSink("7");
        using (var live = new HttpFetcher(new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound))) { RawSink = sink })
            await live.TryGetBytesAsync("https://sos.example.gov/not-yet.pdf", FetchTag.Of("list"));

        var list = new CaptureReader(sink.Directory).Require("list");

        Assert.False(list.HasPayload);
        Assert.Equal(404, list.Status);
        Assert.Throws<InvalidOperationException>(() => list.Bytes());
    }

    [Fact]
    public void Reader_RefusesAPayloadThatNoLongerMatchesItsHash()
    {
        var sink = NewSink("7");
        var entry = sink.Record("GET", "https://sos.example.gov/a", null, 200, "text/plain", "original"u8.ToArray(), 5, null, FetchTag.Of("a"));
        File.WriteAllText(Path.Combine(sink.Directory, entry.FileName!), "edited");

        Assert.Throws<InvalidOperationException>(() => new CaptureReader(sink.Directory).Require("a").Text());
    }

    [Fact]
    public void Manifest_GetsTheHashesOfPayloadsFetchedFromEachUrl()
    {
        var sink = NewSink("1");
        sink.Record("POST", "https://sos.example.gov/api", "r1", 200, "application/json", [1], 5, null);
        sink.Record("POST", "https://sos.example.gov/api", "r2", 200, "application/json", [2], 5, null);
        sink.Record("GET", "https://sos.example.gov/missing", null, 404, null, null, 5, null);
        var manifest = new SourcesManifest
        {
            StatewideCandidates = [new SourceEntry("https://sos.example.gov/api", "json")],
            VerificationOnly = [new SourceEntry("https://ballotpedia.org/x", "html")],
        };

        manifest.AttachPayloadHashes(sink.Entries);

        Assert.Equal([sink.Entries[0].PayloadSha256!, sink.Entries[1].PayloadSha256!], manifest.StatewideCandidates[0].PayloadSha256);
        Assert.Empty(manifest.VerificationOnly[0].PayloadSha256);
    }

    [Fact]
    public void Retention_KeepsTheNewestCaptures()
    {
        var stateDir = Path.Combine(_root, "wv");
        var start = DateTime.UtcNow.AddHours(-1);
        foreach (var (id, minutes) in new[] { ("1", 0), ("2", 10), ("dry-x", 20), ("3", 30) })
        {
            var sink = new RawSink(Path.Combine(stateDir, id), id, "WV", 2026);
            File.SetLastWriteTimeUtc(sink.LogPath, start.AddMinutes(minutes));
        }

        var removed = RawRetention.Prune(stateDir, keep: 2);

        Assert.Equal(["1", "2"], removed.Select(Path.GetFileName).Order());
        Assert.Equal(["3", "dry-x"], Directory.GetDirectories(stateDir).Select(Path.GetFileName).Order());
        Assert.Empty(RawRetention.Prune(stateDir, keep: 0));
    }

    private RawSink NewSink(string captureId) =>
        new(Path.Combine(_root, "wv", captureId), captureId, "wv", 2026);

    private static HttpResponseMessage Ok(string body, string contentType)
    {
        var content = new ByteArrayContent(
            contentType.Contains("iso-8859-1") ? Encoding.Latin1.GetBytes(body) : Encoding.UTF8.GetBytes(body));
        content.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse(contentType);
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(respond(request));
    }
}
