using System.Diagnostics;
using System.Net.Http.Json;
using System.Text.Json;
using StateBallot.Core.Raw;

namespace StateBallot.Core;

/// <summary>HttpClient wrapper with a polite User-Agent, retries, and throttling.</summary>
public sealed class HttpFetcher : IDisposable
{
    private readonly HttpClient _http;
    private readonly TimeSpan _delayBetweenRequests = TimeSpan.FromMilliseconds(250);
    private DateTime _lastRequestUtc = DateTime.MinValue;

    /// <param name="handler">Replaces the network handler, for tests.</param>
    public HttpFetcher(HttpMessageHandler? handler = null)
    {
        handler ??= new HttpClientHandler
        {
            // Decompress gzip/deflate/br responses (Wayback Machine captures send Content-Encoding regardless of Accept-Encoding).
            AutomaticDecompression = System.Net.DecompressionMethods.All,
        };
        _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(60) };
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(
            "StateBallotRoster/1.0 (+civic data collection; contact site operator via repository)");
        _http.DefaultRequestHeaders.Accept.ParseAdd("text/html,application/json;q=0.9,*/*;q=0.8");
    }

    /// <summary>
    /// Adds/overrides a default request header. For a source that needs
    /// browser-like headers (e.g. a Cloudflare-fronted API expecting Origin/Referer),
    /// a collector calls this once right after construction.
    /// Mutates this instance for every subsequent request - callers must not share one
    /// HttpFetcher across collectors for different sources within the same run.
    /// </summary>
    public void AddDefaultHeader(string name, string value)
    {
        _http.DefaultRequestHeaders.Remove(name);
        _http.DefaultRequestHeaders.TryAddWithoutValidation(name, value);
    }

    /// <summary>
    /// Optional rewrite applied to every outgoing URL just before the request is
    /// sent (e.g. Wayback Machine replay). Scrapers and provenance keep seeing
    /// the original source URLs; only the wire request is redirected.
    /// </summary>
    public Func<string, string>? RewriteUrl { get; set; }

    private string Rewrite(string url) => RewriteUrl?.Invoke(url) ?? url;

    /// <summary>
    /// When set, every fetch is written to disk before it is parsed, failures included. The
    /// optional tag on each call names the payload for the normalize stage.
    /// </summary>
    public RawSink? RawSink { get; set; }

    public async Task<string> GetStringAsync(string url, FetchTag? tag = null)
    {
        var fetched = await FetchAsync("GET", url, null, tag, () => _http.GetAsync(Rewrite(url)), nullOn4xx: false);
        return PayloadText.Decode(fetched!.Payload, fetched.ContentType);
    }

    /// <summary>POSTs a JSON-serialized body; returns the raw response string (caller deserializes).</summary>
    public async Task<string> PostJsonAsync<TRequest>(string url, TRequest body, FetchTag? tag = null)
    {
        // Same bytes PostAsJsonAsync sends, so the log identifies the request by its body.
        var requestSha = RawSink.Sha256(JsonSerializer.SerializeToUtf8Bytes(body, WebJson));
        var fetched = await FetchAsync("POST", url, requestSha, tag, () => _http.PostAsJsonAsync(Rewrite(url), body), nullOn4xx: false);
        return PayloadText.Decode(fetched!.Payload, fetched.ContentType);
    }

    /// <summary>Fetches binary content (e.g. a PDF), with the same retry/throttle behavior.</summary>
    public async Task<byte[]> GetBytesAsync(string url, FetchTag? tag = null)
    {
        var bytes = await TryGetBytesAsync(url, tag);
        if (bytes is null)
            throw new InvalidOperationException($"Fetch of {url} was rejected by the server (4xx).");
        return bytes;
    }

    /// <summary>
    /// Like <see cref="GetBytesAsync"/>, but returns null when the server answers
    /// with a 4xx status (e.g. a document that is not published yet). Transient
    /// network errors and 5xx responses are still retried and then thrown.
    /// </summary>
    public async Task<byte[]?> TryGetBytesAsync(string url, FetchTag? tag = null)
    {
        var fetched = await FetchAsync("GET", url, null, tag, () => _http.GetAsync(Rewrite(url)), nullOn4xx: true);
        return fetched?.Payload;
    }

    private sealed record Fetched(byte[] Payload, string? ContentType);

    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Sends with throttle and retries, and records the final outcome of the request in
    /// <see cref="RawSink"/>. Returns null for a 4xx only when the caller tolerates one.
    /// </summary>
    private async Task<Fetched?> FetchAsync(
        string method, string url, string? requestSha, FetchTag? tag, Func<Task<HttpResponseMessage>> send, bool nullOn4xx)
    {
        var sinceLast = DateTime.UtcNow - _lastRequestUtc;
        if (sinceLast < _delayBetweenRequests)
            await Task.Delay(_delayBetweenRequests - sinceLast);

        var clock = Stopwatch.StartNew();
        Exception? last = null;
        int? status = null;
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            status = null;
            try
            {
                _lastRequestUtc = DateTime.UtcNow;
                using var response = await send();
                status = (int)response.StatusCode;
                if (nullOn4xx && status is >= 400 and < 500)
                {
                    RawSink?.Record(method, url, requestSha, status, null, null, clock.ElapsedMilliseconds, null, tag);
                    return null;
                }
                response.EnsureSuccessStatusCode();
                var payload = await response.Content.ReadAsByteArrayAsync();
                var contentType = response.Content.Headers.ContentType?.ToString();
                RawSink?.Record(method, url, requestSha, status, contentType, payload, clock.ElapsedMilliseconds, null, tag);
                return new Fetched(payload, contentType);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                last = ex;
                if (attempt < 3)
                    await Task.Delay(TimeSpan.FromSeconds(2 * attempt));
            }
        }

        RawSink?.Record(method, url, requestSha, status, null, null, clock.ElapsedMilliseconds, last?.Message, tag);
        throw new InvalidOperationException($"Failed to fetch {url} after 3 attempts.", last);
    }

    public void Dispose() => _http.Dispose();
}
