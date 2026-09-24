namespace StateBallot.Core.Raw;

public static class RawRetention
{
    /// <summary>data/raw/&lt;xx&gt;/ under the pipeline data root.</summary>
    public static string StateDir(string pipelineDataRoot, string stateCode) =>
        Path.Combine(pipelineDataRoot, "raw", stateCode.ToLowerInvariant());

    /// <summary>
    /// Deletes all but the newest <paramref name="keep"/> capture directories for a state,
    /// newest by when fetch_log.json was last written. Zero keeps everything.
    /// Returns the directories removed.
    /// </summary>
    public static IReadOnlyList<string> Prune(string stateRawDir, int keep)
    {
        if (keep <= 0 || !Directory.Exists(stateRawDir))
            return [];

        var removed = Directory.GetDirectories(stateRawDir)
            .Select(d => (Dir: d, Written: LastWritten(d)))
            .OrderByDescending(d => d.Written)
            .ThenByDescending(d => d.Dir, StringComparer.Ordinal)
            .Skip(keep)
            .Select(d => d.Dir)
            .ToList();

        foreach (var dir in removed)
            Directory.Delete(dir, recursive: true);
        return removed;
    }

    private static DateTime LastWritten(string dir)
    {
        var log = Path.Combine(dir, RawSink.LogFileName);
        return File.Exists(log) ? File.GetLastWriteTimeUtc(log) : Directory.GetLastWriteTimeUtc(dir);
    }
}
