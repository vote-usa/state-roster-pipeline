using System.Net.Http.Json;

namespace StateBallot.Core;

/// <summary>HttpClient wrapper with a polite User-Agent, retries, and throttling.</summary>
public sealed class HttpFetcher : IDisposable
{
    private readonly HttpClient _http;
    private readonly TimeSpan _delayBetweenRequests = TimeSpan.FromMilliseconds(250);
    private DateTime _lastRequestUtc = DateTime.MinValue;

    public HttpFetcher()
    {
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
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

    public async Task<string> GetStringAsync(string url) =>
        await SendWithRetryAsync(() => _http.GetAsync(url), url);

    /// <summary>POSTs a JSON-serialized body; returns the raw response string (caller deserializes).</summary>
    public async Task<string> PostJsonAsync<TRequest>(string url, TRequest body) =>
        await SendWithRetryAsync(() => _http.PostAsJsonAsync(url, body), url);

    private async Task<string> SendWithRetryAsync(Func<Task<HttpResponseMessage>> send, string url)
    {
        var sinceLast = DateTime.UtcNow - _lastRequestUtc;
        if (sinceLast < _delayBetweenRequests)
            await Task.Delay(_delayBetweenRequests - sinceLast);

        Exception? last = null;
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                _lastRequestUtc = DateTime.UtcNow;
                using var response = await send();
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsStringAsync();
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                last = ex;
                if (attempt < 3)
                    await Task.Delay(TimeSpan.FromSeconds(2 * attempt));
            }
        }

        throw new InvalidOperationException($"Failed to fetch {url} after 3 attempts.", last);
    }

    /// <summary>Fetches binary content (e.g. a PDF), with the same retry/throttle behavior.</summary>
    public async Task<byte[]> GetBytesAsync(string url)
    {
        var bytes = await TryGetBytesAsync(url);
        if (bytes is null)
            throw new InvalidOperationException($"Fetch of {url} was rejected by the server (4xx).");
        return bytes;
    }

    /// <summary>
    /// Like <see cref="GetBytesAsync"/>, but returns null when the server answers
    /// with a 4xx status (e.g. a document that is not published yet). Transient
    /// network errors and 5xx responses are still retried and then thrown.
    /// </summary>
    public async Task<byte[]?> TryGetBytesAsync(string url)
    {
        var sinceLast = DateTime.UtcNow - _lastRequestUtc;
        if (sinceLast < _delayBetweenRequests)
            await Task.Delay(_delayBetweenRequests - sinceLast);

        Exception? last = null;
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                _lastRequestUtc = DateTime.UtcNow;
                using var response = await _http.GetAsync(url);
                if ((int)response.StatusCode is >= 400 and < 500)
                    return null;
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsByteArrayAsync();
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                last = ex;
                if (attempt < 3)
                    await Task.Delay(TimeSpan.FromSeconds(2 * attempt));
            }
        }

        throw new InvalidOperationException($"Failed to fetch {url} after 3 attempts.", last);
    }

    public void Dispose() => _http.Dispose();
}
