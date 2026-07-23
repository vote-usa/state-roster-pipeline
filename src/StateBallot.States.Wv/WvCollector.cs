using System.Globalization;
using StateBallot.Core;

namespace StateBallot.States.Wv;

/// <summary>West Virginia state collector, backed by the WV SOS candidate-web-api.</summary>
public sealed class WvCollector : IStateCollector
{
    private readonly HttpFetcher _fetcher;
    private readonly WvSourceConfig _config;
    private readonly int _year;

    public string StateCode => "WV";

    // stateDataDir is unused here - accepted only to match Runner.cs's shared
    // IStateCollector factory signature (WA needs it for county_fips.json; WV doesn't
    // have any per-state reference data).
    public WvCollector(HttpFetcher fetcher, int year, string stateDataDir, WvSourceConfig? config = null)
    {
        _fetcher = fetcher;
        _year = year;
        _config = config ?? new WvSourceConfig();
    }

    public async Task<CollectResult> CollectAsync()
    {
        Console.WriteLine($"Collecting West Virginia ballot roster for {_year}...");

        var candidateClient = new WvCandidateClient(_fetcher, _config);

        // WV's API takes electionYear as a server-side filter with no further date
        // granularity (no "upcoming only" option), unlike WA/TX's dropdown/catalog
        // sources - so, same as those states' whole-year back-fill mode, every
        // election the API returns for the year is taken as-is.
        var rawCandidates = await candidateClient.FetchAllAsync(_year);
        Console.WriteLine($"  Candidates returned for {_year}: {rawCandidates.Count}");

        if (rawCandidates.Count == 0)
            throw new InvalidOperationException(
                $"No candidates returned for {_year} from {_config.CandidatesUrl}. " +
                "If this is early in the year, filing may not be open yet.");

        var deduped = Deduplicator.RemoveDuplicates(rawCandidates, WvCandidateMapper.DeduplicationKey);
        Console.WriteLine($"  After deduplication: {deduped.Count}");

        var elections = deduped
            .GroupBy(c => c.ElectionId)
            .Select(g => WvCandidateMapper.ToElection(g.First()))
            .OrderBy(e => e.ElectionDate)
            .ThenBy(e => e.ElectionId, StringComparer.Ordinal)
            .ToList();

        var result = new CollectResult();
        foreach (var election in elections)
            result.Elections.Add(election);

        var electionsById = elections.ToDictionary(e => e.ElectionId, StringComparer.Ordinal);
        foreach (var candidate in deduped)
        {
            var election = electionsById[candidate.ElectionId.ToString(CultureInfo.InvariantCulture)];
            result.Candidates.Add(WvCandidateMapper.ToCandidateData(candidate, election, _config.CandidatesUrl));
        }

        result.Candidates.Sort(CompareCandidates);
        BuildSourcesManifest(result);
        return result;
    }

    private static int CompareCandidates(CandidateData a, CandidateData b)
    {
        var c = string.CompareOrdinal(a.ElectionDate, b.ElectionDate);
        if (c != 0) return c;
        c = string.CompareOrdinal(a.Office, b.Office);
        return c != 0 ? c : string.CompareOrdinal(a.CandidateName, b.CandidateName);
    }

    private void BuildSourcesManifest(CollectResult result)
    {
        result.SourceGroups["statewide_candidates"] = new[]
        {
            new { url = _config.CandidatesUrl, format = "json (POST, paginated)" },
        };
        result.SourceGroups["verification_only"] = new[]
        {
            new { url = "https://ballotpedia.org/West_Virginia_elections," + _year, format = "html" },
        };
        result.NextRun = new
        {
            recommended_after = $"{_year + 1}-01-01",
            reason = $"All {_year} candidates collected. Re-run once {_year + 1} filings open.",
        };
    }
}
