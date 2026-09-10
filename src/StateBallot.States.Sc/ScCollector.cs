using System.Text.Json;
using StateBallot.Core;

namespace StateBallot.States.Sc;

/// <summary>
/// South Carolina state collector, backed by the SC Votes (VREMS) candidate
/// portal. Both the primary and general elections' ids, names, and real
/// dates come from one JSON API call (GetElections) - no separate
/// election-date source needed, unlike every other state onboarded so far.
/// The candidate search itself is a stateless form POST (no session/cookie/
/// antiforgery-token setup needed, confirmed live) that returns every
/// candidate for that election in one un-paginated HTML table - both parties
/// together despite looking, from the outside research spreadsheet alone,
/// like a per-party split source (see ScSourceConfig/ScCollector docs and
/// the write-up in configDrivenPipelineInfo.md - same lesson NM's own
/// "looked split, wasn't" correction already taught). v1 scope: the
/// statewide primary and general only - same-cycle special elections and
/// off-cycle local elections (a separate "Local" election kind) are not
/// attempted, nor is the parallel referendum search the same portal offers.
/// </summary>
[StateCode("SC")]
public sealed class ScCollector : IStateCollector
{
    private readonly HttpFetcher _fetcher;
    private readonly ScSourceConfig _config;
    private readonly IPublishSchedule _schedule;
    private readonly int _year;
    private readonly string _stateDataDir;

    public string StateCode => "SC";

    /// <param name="stateDataDir">Per-state output directory (data/output/&lt;xx&gt;/). Inputs are under data/input/&lt;xx&gt;/.</param>
    public ScCollector(HttpFetcher fetcher, int year, string stateDataDir, ScSourceConfig? config = null)
    {
        _fetcher = fetcher;
        _year = year;
        _stateDataDir = stateDataDir;
        _config = config ?? new ScSourceConfig();
        _schedule = new ScPublishSchedule();
    }

    public async Task<CollectResult> CollectAsync()
    {
        Console.WriteLine($"Collecting South Carolina ballot roster for {_year}...");

        var (dataRoot, _) = DataPaths.FromStateOutputDir(_stateDataDir);
        var fieldMap = LookupTableLoader.Load(DataPaths.CandidateFieldMapPath(dataRoot, StateCode));

        var electionsUrl = _config.ElectionsByYearUrl(_year);
        var electionsJson = await _fetcher.GetStringAsync(electionsUrl);
        var available = JsonSerializer.Deserialize<List<ScElection>>(electionsJson) ?? [];
        ScrapeGuard.RequireAny(available, () => $"No elections at all returned from {electionsUrl} for {_year}.");

        var result = new CollectResult();
        var candidateSourceUrls = new List<SourceEntry>();

        foreach (var (type, name) in new[] { ("Primary", ScSelectors.PrimaryElectionName), ("General", ScSelectors.GeneralElectionName) })
        {
            var found = available.FirstOrDefault(e => string.Equals(e.ElectionName, name, StringComparison.OrdinalIgnoreCase));
            if (found is null)
            {
                result.Gaps.Add($"'{name}' not found among {_year}'s elections at {electionsUrl}. Re-run later or check the source manually.");
                continue;
            }

            var electionDate = DateOnly.FromDateTime(found.ElectionDate);
            var election = ScCandidateMapper.ToElection(type, electionDate, found.ElectionId, electionsUrl);
            RowHelpers.StampState(election, StateCode);
            result.Elections.Add(election);

            var html = await _fetcher.PostFormAsync(
                _config.CandidateSearchUrl, new Dictionary<string, string> { ["ElectionId"] = found.ElectionId });
            var rows = HtmlTableParser.Parse(html);
            var sourceUrl = $"{_config.CandidateSearchUrl}?ElectionId={found.ElectionId}";

            if (rows.Count == 0)
            {
                result.Gaps.Add(
                    $"{election.Name} ({electionDate:yyyy-MM-dd}): no rows parsed from {sourceUrl}. Re-run later.");
                result.PendingElections.Add(election);
                continue;
            }

            candidateSourceUrls.Add(new SourceEntry(sourceUrl, "html"));
            foreach (var row in rows)
            {
                var candidate = ScCandidateMapper.ToCandidateRow(row, election, sourceUrl, fieldMap);
                RowHelpers.StampState(candidate, StateCode);
                result.Candidates.Add(candidate);
            }

            Console.WriteLine($"  {election.Name} ({electionDate:yyyy-MM-dd}): {rows.Count} candidates ({sourceUrl})");
        }

        if (result.Candidates.Count == 0)
            throw new InvalidOperationException(
                $"No candidates collected for any discovered election; refusing to write hollow outputs. Check {electionsUrl} manually.");

        CollectResultSorter.Sort(result);
        BuildSourcesManifest(result, electionsUrl, candidateSourceUrls);
        return result;
    }

    private void BuildSourcesManifest(CollectResult result, string electionsUrl, List<SourceEntry> candidateListUrls)
    {
        var sources = result.Sources;
        sources.Elections = [new SourceEntry(electionsUrl, "json")];
        sources.StatewideCandidates = candidateListUrls;
        sources.VerificationOnly = [new SourceEntry($"https://ballotpedia.org/South_Carolina_elections,_{_year}", "html")];
        sources.NextRun = _schedule.Recommend(result, _year);
    }
}
