using System.Globalization;
using StateBallot.Core;

namespace StateBallot.States.Wv;

/// <summary>West Virginia state collector, backed by the WV SOS candidate-web-api.</summary>
[StateCode("WV")]
public sealed class WvCollector : IStateCollector
{
    private readonly HttpFetcher _fetcher;
    private readonly WvSourceConfig _config;
    private readonly IPublishSchedule _schedule;
    private readonly int _year;

    public string StateCode => "WV";

    /// <param name="stateDataDir">Per-state output directory (data/output/&lt;xx&gt;/). Inputs are under data/input/&lt;xx&gt;/.</param>
    // Unused today: WV has no per-state input files (no county_fips.json). Kept to match
    // Runner.cs's shared IStateCollector factory signature.
    public WvCollector(HttpFetcher fetcher, int year, string stateDataDir, WvSourceConfig? config = null)
    {
        _fetcher = fetcher;
        _year = year;
        _config = config ?? new WvSourceConfig();
        _schedule = new WvPublishSchedule();
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
        result.Elections.AddRange(elections);

        var electionsById = elections.ToDictionary(e => e.ElectionId, StringComparer.Ordinal);
        foreach (var candidate in deduped)
        {
            var election = electionsById[candidate.ElectionId.ToString(CultureInfo.InvariantCulture)];
            result.Candidates.Add(WvCandidateMapper.ToCandidateRow(candidate, election, _config.CandidatesUrl));
        }

        CollectResultSorter.Sort(result);
        BuildSourcesManifest(result);
        return result;
    }

    private void BuildSourcesManifest(CollectResult result)
    {
        var sources = result.Sources;
        sources.StatewideCandidates = [new SourceEntry(_config.CandidatesUrl, "json (POST, paginated)")];
        sources.VerificationOnly = [new SourceEntry("https://ballotpedia.org/West_Virginia_elections," + _year, "html")];
        sources.NextRun = _schedule.Recommend(result, _year);
    }
}
