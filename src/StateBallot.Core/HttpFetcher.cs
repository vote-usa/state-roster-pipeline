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

    public async Task<string> GetStringAsync(string url)
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

    public void Dispose() => _http.Dispose();
}
