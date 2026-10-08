using System.Net.Http.Headers;

namespace StateBallot.Core.Raw;

/// <summary>One captured fetch as the normalize stage sees it.</summary>
public sealed class CapturedFetch
{
    private readonly string _directory;

    internal CapturedFetch(string directory, FetchLogEntry entry)
    {
        _directory = directory;
        Entry = entry;
    }

    public FetchLogEntry Entry { get; }
    public string Url => Entry.Url;
    public int? Status => Entry.Status;

    /// <summary>False for a 4xx or a fetch that failed every retry.</summary>
    public bool HasPayload => Entry.FileName is not null;

    public string Key(string name) =>
        Entry.Keys is not null && Entry.Keys.TryGetValue(name, out var value)
            ? value
            : throw new InvalidOperationException($"Captured {Entry.Role} fetch {Entry.Seq} has no '{name}' key.");

    public byte[] Bytes()
    {
        if (Entry.FileName is null)
            throw new InvalidOperationException(
                $"Captured fetch {Entry.Seq} ({Entry.Method} {Entry.Url}) has no payload: " +
                (Entry.Status is { } s ? $"HTTP {s}" : Entry.Error ?? "no response") + ".");

        var path = Path.Combine(_directory, Entry.FileName);
        var bytes = File.ReadAllBytes(path);
        if (RawSink.Sha256(bytes) != Entry.PayloadSha256)
            throw new InvalidOperationException($"{path} does not match the SHA-256 in {RawSink.LogFileName}.");
        return bytes;
    }

    /// <summary>The payload decoded with its response charset, as the fetcher would have returned it.</summary>
    public string Text() => PayloadText.Decode(Bytes(), Entry.ContentType);
}

/// <summary>
/// Reads a capture directory for the normalize stage. Payloads are looked up by the role
/// and keys the capture stage tagged them with, never fetched.
/// </summary>
public sealed class CaptureReader
{
    public CaptureReader(string directory)
    {
        Directory = directory;
        Log = RawSink.ReadLog(directory);
    }

    public string Directory { get; }
    public FetchLog Log { get; }
    public string CaptureId => Log.CaptureId;
    public int Year => Log.Year;

    /// <summary>The day the capture ran, which is "today" for upcoming-election filtering.</summary>
    public DateOnly AsOf => DateOnly.FromDateTime(Log.StartedAt);

    /// <summary>Every fetch with this role, in capture order.</summary>
    public IReadOnlyList<CapturedFetch> All(string role) =>
        Log.Fetches.Where(f => f.Role == role).OrderBy(f => f.Seq).Select(f => new CapturedFetch(Directory, f)).ToList();

    /// <summary>The one fetch with this role and keys, or null when the capture stage did not make it.</summary>
    public CapturedFetch? Find(string role, params (string Key, string Value)[] keys)
    {
        var matches = Log.Fetches
            .Where(f => f.Role == role && keys.All(k => f.Keys is not null && f.Keys.TryGetValue(k.Key, out var v) && v == k.Value))
            .ToList();
        if (matches.Count > 1)
            throw new InvalidOperationException(
                $"Capture {CaptureId} has {matches.Count} {Describe(role, keys)} fetches (seq {string.Join(", ", matches.Select(m => m.Seq))}), expected one.");
        return matches.Count == 0 ? null : new CapturedFetch(Directory, matches[0]);
    }

    /// <summary>Like <see cref="Find"/>, but a missing fetch means the capture and the normalizer disagree.</summary>
    public CapturedFetch Require(string role, params (string Key, string Value)[] keys) =>
        Find(role, keys) ?? throw new InvalidOperationException(
            $"Capture {CaptureId} has no {Describe(role, keys)} fetch. The capture stage and the normalizer disagree about what was fetched.");

    private static string Describe(string role, (string Key, string Value)[] keys) =>
        keys.Length == 0 ? $"'{role}'" : $"'{role}' ({string.Join(", ", keys.Select(k => $"{k.Key}={k.Value}"))})";
}

public static class PayloadText
{
    /// <summary>Decodes with the Content-Type charset, the same way HttpContent.ReadAsStringAsync does.</summary>
    public static string Decode(byte[] payload, string? contentType)
    {
        using var content = new ByteArrayContent(payload);
        if (contentType is not null && MediaTypeHeaderValue.TryParse(contentType, out var mediaType))
            content.Headers.ContentType = mediaType;
        return content.ReadAsStringAsync().GetAwaiter().GetResult();
    }
}
