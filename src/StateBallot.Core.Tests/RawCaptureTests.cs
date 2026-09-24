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
    public async Task Replay_ServesTheCapturedBytesWithoutTheNetwork()
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
            await live.GetStringAsync("https://sos.example.gov/");
            await live.PostJsonAsync("https://sos.example.gov/api", new { Page = 1 });
            await live.PostJsonAsync("https://sos.example.gov/api", new { Page = 2 });
            await live.GetBytesAsync("https://sos.example.gov/doc.pdf");
        }

        using var offline = new HttpFetcher(new FakeHandler(_ => throw new InvalidOperationException("network used")))
        {
            Replay = new RawReplay(sink.Directory),
        };

        Assert.Equal("café", await offline.GetStringAsync("https://sos.example.gov/"));
        Assert.Equal("{\"page\":{\"page\":2}}", await offline.PostJsonAsync("https://sos.example.gov/api", new { Page = 2 }));
        Assert.Equal("{\"page\":{\"page\":1}}", await offline.PostJsonAsync("https://sos.example.gov/api", new { Page = 1 }));
        Assert.Equal(pdf, await offline.GetBytesAsync("https://sos.example.gov/doc.pdf"));
        Assert.Equal(0, offline.Replay!.UnusedCount);
    }

    [Fact]
    public async Task Replay_OfAUrlTheCaptureNeverFetched_ThrowsNamingIt()
    {
        var sink = NewSink("7");
        using (var live = new HttpFetcher(new FakeHandler(_ => Ok("x", "text/plain"))) { RawSink = sink })
            await live.GetStringAsync("https://sos.example.gov/a");

        using var offline = new HttpFetcher(new FakeHandler(_ => throw new InvalidOperationException("network used")))
        {
            Replay = new RawReplay(sink.Directory),
        };

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => offline.GetStringAsync("https://sos.example.gov/b"));
        Assert.Contains("https://sos.example.gov/b", ex.Message);
    }

    [Fact]
    public async Task Replay_ReproducesA4xx()
    {
        var sink = NewSink("7");
        using (var live = new HttpFetcher(new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound))) { RawSink = sink })
            await live.TryGetBytesAsync("https://sos.example.gov/not-yet.pdf");

        using var offline = new HttpFetcher(new FakeHandler(_ => throw new InvalidOperationException("network used")))
        {
            Replay = new RawReplay(sink.Directory),
        };

        Assert.Null(await offline.TryGetBytesAsync("https://sos.example.gov/not-yet.pdf"));
    }

    [Fact]
    public async Task Replay_RefusesAPayloadThatNoLongerMatchesItsHash()
    {
        var sink = NewSink("7");
        using (var live = new HttpFetcher(new FakeHandler(_ => Ok("original", "text/plain"))) { RawSink = sink })
            await live.GetStringAsync("https://sos.example.gov/a");
        File.WriteAllText(Path.Combine(sink.Directory, sink.Entries[0].FileName!), "edited");

        using var offline = new HttpFetcher { Replay = new RawReplay(sink.Directory) };

        await Assert.ThrowsAsync<InvalidOperationException>(() => offline.GetStringAsync("https://sos.example.gov/a"));
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
