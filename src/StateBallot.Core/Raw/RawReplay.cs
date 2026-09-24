namespace StateBallot.Core.Raw;

/// <summary>
/// Serves a capture directory back to the fetcher instead of the network. Requests are
/// matched on method, URL and request body hash. Repeats of one request are served in
/// the order they were captured, and the last one again once the captured copies run out.
/// A request the capture never made throws, naming the URL.
/// </summary>
public sealed class RawReplay
{
    private readonly Dictionary<(string Method, string Url, string? RequestSha256), Queue<FetchLogEntry>> _pending = [];
    private readonly Dictionary<(string, string, string?), FetchLogEntry> _last = [];
    private readonly List<FetchLogEntry> _served = [];

    public RawReplay(string directory)
    {
        Directory = directory;
        Log = RawSink.ReadLog(directory);
        foreach (var entry in Log.Fetches.OrderBy(f => f.Seq))
        {
            var key = (entry.Method, entry.Url, entry.RequestSha256);
            if (!_pending.TryGetValue(key, out var queue))
                _pending[key] = queue = new Queue<FetchLogEntry>();
            queue.Enqueue(entry);
        }
    }

    public string Directory { get; }
    public FetchLog Log { get; }

    /// <summary>Captured entries handed out so far, in request order.</summary>
    public IReadOnlyList<FetchLogEntry> Served => _served;

    /// <summary>Captured requests the replay never asked for.</summary>
    public int UnusedCount => _pending.Values.Sum(q => q.Count);

    public (FetchLogEntry Entry, byte[]? Payload) Next(string method, string url, string? requestSha256)
    {
        var key = (method, url, requestSha256);
        FetchLogEntry entry;
        if (_pending.TryGetValue(key, out var queue) && queue.Count > 0)
            entry = queue.Dequeue();
        else if (!_last.TryGetValue(key, out entry!))
            throw new InvalidOperationException(
                $"Replay of capture {Log.CaptureId} has no {method} {url}" +
                (requestSha256 is null ? "" : $" with request body {requestSha256}") +
                ". The original pass never made this request.");

        _last[key] = entry;
        _served.Add(entry);

        if (entry.FileName is null)
            return (entry, null);

        var payload = File.ReadAllBytes(Path.Combine(Directory, entry.FileName));
        if (RawSink.Sha256(payload) != entry.PayloadSha256)
            throw new InvalidOperationException(
                $"{Path.Combine(Directory, entry.FileName)} does not match the SHA-256 in {RawSink.LogFileName}.");
        return (entry, payload);
    }
}
