namespace StateBallot.Core;

/// <summary>One URL + retrieval format entry in sources.json.</summary>
public sealed class SourceEntry
{
    public string Url { get; set; } = "";
    public string Format { get; set; } = "";

    /// <summary>SHA-256 of each payload fetched from this URL, in fetch order.</summary>
    public List<string> PayloadSha256 { get; set; } = new();

    public SourceEntry() { }

    public SourceEntry(string url, string format)
    {
        Url = url;
        Format = format;
    }
}

/// <summary>Machine-readable re-run recommendation written into sources.json.</summary>
public sealed class NextRunInfo
{
    public string RecommendedAfter { get; set; } = "";
    public string Reason { get; set; } = "";
    public string? NextElectionDate { get; set; }
    public string? NextElectionType { get; set; }
}

/// <summary>
/// Typed provenance manifest. Serialized to sources.json with the same key names
/// every state has historically produced.
/// </summary>
public sealed class SourcesManifest
{
    public List<SourceEntry> Elections { get; set; } = new();
    public List<SourceEntry> StatewideCandidates { get; set; } = new();
    public List<SourceEntry> StatewideMeasures { get; set; } = new();
    public List<SourceEntry> LocalMeasures { get; set; } = new();
    public List<SourceEntry> CountyDirectory { get; set; } = new();

    /// <summary>County name => source entries for that county's ballots.</summary>
    public Dictionary<string, List<SourceEntry>> CountyBallots { get; set; } =
        new(StringComparer.Ordinal);

    public List<SourceEntry> VerificationOnly { get; set; } = new();
    public NextRunInfo? NextRun { get; set; }

    /// <summary>
    /// Fills each entry's payload hashes from the fetches whose URL matches it exactly.
    /// An entry that names a page the pass never fetched keeps an empty list.
    /// </summary>
    public void AttachPayloadHashes(IEnumerable<Raw.FetchLogEntry> fetches)
    {
        var byUrl = fetches
            .Where(f => f.PayloadSha256 is not null)
            .OrderBy(f => f.Seq)
            .GroupBy(f => f.Url, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(f => f.PayloadSha256!).Distinct().ToList(), StringComparer.Ordinal);

        var entries = Elections.Concat(StatewideCandidates).Concat(StatewideMeasures).Concat(LocalMeasures)
            .Concat(CountyDirectory).Concat(VerificationOnly).Concat(CountyBallots.Values.SelectMany(v => v));
        foreach (var entry in entries)
            entry.PayloadSha256 = byUrl.TryGetValue(entry.Url, out var hashes) ? [.. hashes] : [];
    }

    /// <summary>Builds the dictionary shape written to sources.json (plus gaps).</summary>
    public Dictionary<string, object?> ToJsonObject(IReadOnlyList<string> gaps)
    {
        var dict = new Dictionary<string, object?>
        {
            ["elections"] = ToAnon(Elections),
            ["statewide_candidates"] = ToAnon(StatewideCandidates),
            ["statewide_measures"] = ToAnon(StatewideMeasures),
            ["county_directory"] = ToAnon(CountyDirectory),
            ["verification_only"] = ToAnon(VerificationOnly),
            ["gaps"] = gaps,
            ["next_run"] = NextRun is null ? null : new
            {
                recommended_after = NextRun.RecommendedAfter,
                reason = NextRun.Reason,
                next_election_date = NextRun.NextElectionDate,
                next_election_type = NextRun.NextElectionType,
            },
        };

        if (LocalMeasures.Count > 0)
            dict["local_measures"] = ToAnon(LocalMeasures);

        if (CountyBallots.Count > 0)
        {
            dict["county_ballots"] = CountyBallots.ToDictionary(
                kv => kv.Key,
                kv => ToAnon(kv.Value),
                StringComparer.Ordinal);
        }

        return dict;
    }

    private static object[] ToAnon(IEnumerable<SourceEntry> entries) =>
        entries.Select(e => (object)new { url = e.Url, format = e.Format, payload_sha256 = e.PayloadSha256 }).ToArray();
}
