using StateBallot.Core;

namespace StateBallot.States.Md;

/// <summary>
/// Maryland state collector, backed by the MD State Board of Elections site.
/// v1 scope: statewide candidates only (Governor through judicial races) for
/// whichever election(s) the SBE's landing page currently lists as upcoming.
/// Deliberately not yet covered: local/county races (MD publishes those in a
/// separate "all_counties" CSV export whose "Contest Run By..." column mixes
/// county names and within-county district labels - attributing each row to
/// its county needs order-dependent parsing not attempted here), ballot
/// measures (no MD ballot-measure source was found), and a county directory.
/// </summary>
[StateCode("MD")]
public sealed class MdCollector : IStateCollector
{
    private readonly HttpFetcher _fetcher;
    private readonly MdSourceConfig _config;
    private readonly IPublishSchedule _schedule;
    private readonly int _year;
    private readonly string _stateDataDir;

    public string StateCode => "MD";

    /// <param name="stateDataDir">Per-state output directory (data/output/&lt;xx&gt;/). Inputs are under data/input/&lt;xx&gt;/,
    /// including date_formats.json.</param>
    public MdCollector(HttpFetcher fetcher, int year, string stateDataDir, MdSourceConfig? config = null)
    {
        _fetcher = fetcher;
        _year = year;
        _stateDataDir = stateDataDir;
        _config = config ?? new MdSourceConfig();
        _schedule = new MdPublishSchedule();
    }

    public async Task<CollectResult> CollectAsync()
    {
        Console.WriteLine($"Collecting Maryland ballot roster for {_year}...");

        var (dataRoot, _) = DataPaths.FromStateOutputDir(_stateDataDir);
        var dateFormats = DateFormatConfig.Load(DataPaths.DateFormatsPath(dataRoot, StateCode));
        var fieldMap = LookupTableLoader.Load(DataPaths.CandidateFieldMapPath(dataRoot, StateCode));

        var elections = await new ElectionDayScraper(_fetcher, _config, dateFormats).FetchAsync(_year);
        Console.WriteLine($"  Election day entries found: {elections.Count}");
        RowHelpers.StampState(elections, StateCode);

        var result = new CollectResult();
        result.Elections.AddRange(elections);

        foreach (var election in elections)
        {
            Console.WriteLine($"  {election.Name} ({election.ElectionDate:yyyy-MM-dd})...");
            var url = _config.StatewideCandidateListUrl(_year, election.ElectionType);
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
                var candidate = MdCandidateMapper.ToCandidateRow(row, election, url, fieldMap);
                RowHelpers.StampState(candidate, StateCode);
                result.Candidates.Add(candidate);
            }

            Console.WriteLine($"    {rows.Count} candidates from statewide list ({url})");
        }

        if (result.Candidates.Count == 0)
            throw new InvalidOperationException(
                "No candidates collected for any discovered election; refusing to write hollow outputs. " +
                "Check https://elections.maryland.gov manually.");

        CollectResultSorter.Sort(result);
        BuildSourcesManifest(result, elections);
        return result;
    }

    private void BuildSourcesManifest(CollectResult result, List<Election> elections)
    {
        var sources = result.Sources;
        sources.Elections = [new SourceEntry(_config.ElectionsPageUrl(_year), "html")];
        sources.StatewideCandidates = elections
            .Select(e => new SourceEntry(_config.StatewideCandidateListUrl(_year, e.ElectionType), "csv"))
            .ToList();
        sources.VerificationOnly = [new SourceEntry($"https://ballotpedia.org/Maryland_elections,_{_year}", "html")];
        sources.NextRun = _schedule.Recommend(result, _year);
    }
}
