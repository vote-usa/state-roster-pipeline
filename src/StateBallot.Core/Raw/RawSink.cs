using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace StateBallot.Core.Raw;

/// <summary>One fetch as recorded in fetch_log.json and PassFetches.</summary>
public sealed class FetchLogEntry
{
    public int Seq { get; set; }
    public DateTime FetchedAt { get; set; }
    public string Method { get; set; } = "";

    /// <summary>The source URL the collector asked for, before any Wayback rewrite.</summary>
    public string Url { get; set; } = "";

    /// <summary>SHA-256 of the request body, for POSTs.</summary>
    public string? RequestSha256 { get; set; }

    /// <summary>HTTP status of the last attempt, null when no response arrived.</summary>
    public int? Status { get; set; }

    public string? ContentType { get; set; }
    public long? Bytes { get; set; }
    public string? PayloadSha256 { get; set; }

    /// <summary>Payload file inside the capture directory, null when nothing was saved.</summary>
    public string? FileName { get; set; }

    public long DurationMs { get; set; }
    public string? Error { get; set; }
}

/// <summary>The fetch_log.json document at the root of a capture directory.</summary>
public sealed class FetchLog
{
    public string CaptureId { get; set; } = "";
    public string State { get; set; } = "";
    public int Year { get; set; }
    public DateTime StartedAt { get; set; }
    public List<FetchLogEntry> Fetches { get; set; } = [];
}

/// <summary>
/// Writes every payload the fetcher receives to data/raw/&lt;xx&gt;/&lt;capture-id&gt;/ before
/// anything parses it, and keeps fetch_log.json beside the payloads. The log is rewritten
/// after every fetch so a pass that dies midway still leaves its evidence behind.
/// Not thread-safe: collectors fetch one request at a time.
/// </summary>
public sealed class RawSink
{
    public const string LogFileName = "fetch_log.json";

    private readonly FetchLog _log;

    public RawSink(string directory, string captureId, string state, int year)
    {
        Directory = directory;
        System.IO.Directory.CreateDirectory(directory);
        _log = new FetchLog
        {
            CaptureId = captureId,
            State = state.ToUpperInvariant(),
            Year = year,
            StartedAt = DateTime.UtcNow,
        };
        WriteLog();
    }

    public string Directory { get; }
    public string LogPath => Path.Combine(Directory, LogFileName);
    public IReadOnlyList<FetchLogEntry> Entries => _log.Fetches;
    public long TotalBytes => _log.Fetches.Sum(f => f.Bytes ?? 0);

    public FetchLogEntry Record(
        string method, string url, string? requestSha256, int? status, string? contentType,
        byte[]? payload, long durationMs, string? error)
    {
        var entry = new FetchLogEntry
        {
            Seq = _log.Fetches.Count + 1,
            FetchedAt = DateTime.UtcNow,
            Method = method,
            Url = url,
            RequestSha256 = requestSha256,
            Status = status,
            ContentType = contentType,
            DurationMs = durationMs,
            Error = error,
        };

        if (payload is not null)
        {
            entry.FileName = $"{entry.Seq:000}-{Slug(url)}.{Extension(contentType)}";
            entry.Bytes = payload.LongLength;
            entry.PayloadSha256 = Sha256(payload);
            File.WriteAllBytes(Path.Combine(Directory, entry.FileName), payload);
        }

        _log.Fetches.Add(entry);
        WriteLog();
        return entry;
    }

    public string LogSha256() => Sha256(File.ReadAllBytes(LogPath));

    public static FetchLog ReadLog(string directory) =>
        JsonSerializer.Deserialize<FetchLog>(File.ReadAllText(Path.Combine(directory, LogFileName)), OutputWriter.JsonOptions)
        ?? throw new InvalidOperationException($"{Path.Combine(directory, LogFileName)} is empty.");

    public static string Sha256(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private void WriteLog() =>
        File.WriteAllText(LogPath, JsonSerializer.Serialize(_log, OutputWriter.JsonOptions) + "\n");

    private static string Slug(string url)
    {
        var path = Uri.TryCreate(url, UriKind.Absolute, out var uri)
            ? uri.Host + uri.AbsolutePath
            : url;
        var slug = Regex.Replace(path.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
        if (slug.Length > 60)
            slug = slug[^60..].TrimStart('-');
        return slug.Length == 0 ? "payload" : slug;
    }

    private static string Extension(string? contentType)
    {
        var media = contentType?.Split(';')[0].Trim().ToLowerInvariant() ?? "";
        return media switch
        {
            "text/html" or "application/xhtml+xml" => "html",
            "application/json" or "text/json" => "json",
            "application/pdf" => "pdf",
            "text/csv" => "csv",
            "application/xml" or "text/xml" => "xml",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" => "xlsx",
            "text/plain" => "txt",
            _ when media.EndsWith("+json") => "json",
            _ => "bin",
        };
    }
}
