using StateBallot.Core;

namespace StateBallot.States.Wy;

/// <summary>
/// Wyoming state collector, backed by the WY SOS elections site. Both the
/// primary and general elections' candidate lists are separate static CSVs
/// (no bot-blocking, no session/auth), and neither carries the election's
/// actual date - that's scraped separately from the info page's JSON-LD (see
/// ElectionDateScraper). v1 scope: candidates only - WY's export has no
/// county concept at all, and its ballot-measure source is full-text-only PDF
/// (not attempted here).
/// </summary>
[StateCode("WY")]
public sealed class WyCollector : IStateCollector
{
    private readonly HttpFetcher _fetcher;
    private readonly WySourceConfig _config;
    private readonly IPublishSchedule _schedule;
    private readonly int _year;
    private readonly string _stateDataDir;

    public string StateCode => "WY";

    /// <param name="stateDataDir">Per-state output directory (data/output/&lt;xx&gt;/). Inputs are under data/input/&lt;xx&gt;/,
    /// including date_formats.json.</param>
    public WyCollector(HttpFetcher fetcher, int year, string stateDataDir, WySourceConfig? config = null)
    {
        _fetcher = fetcher;
        _year = year;
        _stateDataDir = stateDataDir;
        _config = config ?? new WySourceConfig();
        _schedule = new WyPublishSchedule();
    }

    public async Task<CollectResult> CollectAsync()
    {
        Console.WriteLine($"Collecting Wyoming ballot roster for {_year}...");

        var (dataRoot, _) = DataPaths.FromStateOutputDir(_stateDataDir);
        var dateFormats = DateFormatConfig.Load(DataPaths.DateFormatsPath(dataRoot, StateCode));

        var elections = await new ElectionDateScraper(_fetcher, _config, dateFormats).FetchAsync(_year);
        Console.WriteLine($"  Elections found: {elections.Count}");
        RowHelpers.StampState(elections, StateCode);

        var result = new CollectResult();
        result.Elections.AddRange(elections);

        foreach (var election in elections)
        {
            var url = _config.CandidateListUrl(_year, election.ElectionType);
            var csvText = await _fetcher.GetStringAsync(url);
            var rows = DelimitedTableParser.Parse(csvText);

            if (rows.Count == 0)
            {
                result.Gaps.Add(
                    $"{election.Name} ({election.ElectionDate:yyyy-MM-dd}): no rows parsed from {url}; " +
                    "the candidate list may not be published yet. Re-run later.");
                result.PendingElections.Add(election);
                continue;
            }

            foreach (var row in rows)
            {
                var candidate = WyCandidateMapper.ToCandidateRow(row, election, url);
                RowHelpers.StampState(candidate, StateCode);
                result.Candidates.Add(candidate);
            }

            Console.WriteLine($"  {election.Name} ({election.ElectionDate:yyyy-MM-dd}): {rows.Count} candidates ({url})");
        }

        if (result.Candidates.Count == 0)
            throw new InvalidOperationException(
                "No candidates collected for any discovered election; refusing to write hollow outputs. " +
                "Check https://sos.wyo.gov manually.");

        CollectResultSorter.Sort(result);
        BuildSourcesManifest(result, elections);
        return result;
    }

    private void BuildSourcesManifest(CollectResult result, List<Election> elections)
    {
        var sources = result.Sources;
        sources.Elections = [new SourceEntry(_config.ElectionInfoPageUrl(_year), "html")];
        sources.StatewideCandidates = elections
            .Select(e => new SourceEntry(_config.CandidateListUrl(_year, e.ElectionType), "csv"))
            .ToList();
        sources.VerificationOnly = [new SourceEntry($"https://ballotpedia.org/Wyoming_elections,_{_year}", "html")];
        sources.NextRun = _schedule.Recommend(result, _year);
    }
}
