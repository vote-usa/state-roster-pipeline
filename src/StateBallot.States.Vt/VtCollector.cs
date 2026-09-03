using StateBallot.Core;

namespace StateBallot.States.Vt;

/// <summary>
/// Vermont state collector, backed by the VT SOS elections site. The
/// Primary/General candidate lists are separate static XLSX files at
/// year-templated, stable URLs (no bot-blocking, no session/auth) - unlike
/// CO's equivalent pages, both back-fill years and the current cycle work the
/// same way. Neither XLSX carries the election's actual date - that's scraped
/// separately from the (evergreen, current-cycle-only) candidates page's own
/// plain text (see VtElectionDateScraper), so only the current cycle's dates
/// are ever available even though past years' candidate rosters are.
/// </summary>
[StateCode("VT")]
public sealed class VtCollector : IStateCollector
{
    private readonly HttpFetcher _fetcher;
    private readonly VtSourceConfig _config;
    private readonly IPublishSchedule _schedule;
    private readonly int _year;
    private readonly string _stateDataDir;

    public string StateCode => "VT";

    /// <param name="stateDataDir">Per-state output directory (data/output/&lt;xx&gt;/). Inputs are under data/input/&lt;xx&gt;/,
    /// including date_formats.json.</param>
    public VtCollector(HttpFetcher fetcher, int year, string stateDataDir, VtSourceConfig? config = null)
    {
        _fetcher = fetcher;
        _year = year;
        _stateDataDir = stateDataDir;
        _config = config ?? new VtSourceConfig();
        _schedule = new VtPublishSchedule();
    }

    public async Task<CollectResult> CollectAsync()
    {
        Console.WriteLine($"Collecting Vermont ballot roster for {_year}...");

        var (dataRoot, _) = DataPaths.FromStateOutputDir(_stateDataDir);
        var dateFormats = DateFormatConfig.Load(DataPaths.DateFormatsPath(dataRoot, StateCode));
        var fieldMap = LookupTableLoader.Load(DataPaths.CandidateFieldMapPath(dataRoot, StateCode));

        var found = await new VtElectionDateScraper(_fetcher, _config, dateFormats).TryFetchAsync(_year)
            ?? throw new InvalidOperationException(
                $"{_config.CandidatesPageUrl} doesn't currently show {_year}'s election dates (it's an evergreen " +
                "page that only ever reflects the current cycle). Back-filling a past year's dates isn't supported by this source.");
        List<Election> elections = [found.Primary, found.General];
        Console.WriteLine($"  Elections found: {elections.Count}");
        RowHelpers.StampState(elections, StateCode);

        var result = new CollectResult();
        result.Elections.AddRange(elections);
        var candidateListUrls = new List<SourceEntry>();

        foreach (var election in elections)
        {
            var xlsxUrl = _config.CandidateListUrl(_year, election.ElectionType);
            candidateListUrls.Add(new SourceEntry(xlsxUrl, "xlsx"));
            var bytes = await _fetcher.TryGetBytesAsync(xlsxUrl);

            if (bytes is null)
            {
                result.Gaps.Add(
                    $"{election.Name} ({election.ElectionDate:yyyy-MM-dd}): {xlsxUrl} not published yet. Re-run later.");
                result.PendingElections.Add(election);
                continue;
            }

            // Every genuine row has a Contest; guards against any stray blank
            // formatting row ClosedXML's used-range picks up ahead of the real header.
            var rows = XlsxTableParser.Parse(bytes)
                .Where(r => !string.IsNullOrWhiteSpace(r.GetValueOrDefault("Contest")))
                .ToList();

            if (rows.Count == 0)
            {
                result.Gaps.Add(
                    $"{election.Name} ({election.ElectionDate:yyyy-MM-dd}): no rows parsed from {xlsxUrl}; " +
                    "the candidate list may not be published yet. Re-run later.");
                result.PendingElections.Add(election);
                continue;
            }

            foreach (var row in rows)
            {
                var candidate = VtCandidateMapper.ToCandidateRow(row, election, xlsxUrl, fieldMap);
                RowHelpers.StampState(candidate, StateCode);
                result.Candidates.Add(candidate);
            }

            Console.WriteLine($"  {election.Name} ({election.ElectionDate:yyyy-MM-dd}): {rows.Count} candidates ({xlsxUrl})");
        }

        if (result.Candidates.Count == 0)
            throw new InvalidOperationException(
                "No candidates collected for any discovered election; refusing to write hollow outputs. " +
                "Check https://sos.vermont.gov/elections/election-info-resources/candidates manually.");

        CollectResultSorter.Sort(result);
        BuildSourcesManifest(result, candidateListUrls);
        return result;
    }

    private void BuildSourcesManifest(CollectResult result, List<SourceEntry> candidateListUrls)
    {
        var sources = result.Sources;
        sources.Elections = [new SourceEntry(_config.CandidatesPageUrl, "html")];
        sources.StatewideCandidates = candidateListUrls;
        sources.VerificationOnly = [new SourceEntry($"https://ballotpedia.org/Vermont_elections,_{_year}", "html")];
        sources.NextRun = _schedule.Recommend(result, _year);
    }
}
