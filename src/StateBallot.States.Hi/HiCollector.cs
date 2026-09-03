using StateBallot.Core;

namespace StateBallot.States.Hi;

/// <summary>
/// Hawaii state collector, backed by the HI Office of Elections sites. One
/// candidate export covers the entire election cycle (both Primary and
/// General); each row's own Status says which one it's actually in (see
/// HiCandidateMapper.DetermineElection), since the export itself carries no
/// per-row election date/id. v1 scope: candidates only - HI's export has no
/// county concept (county-level races are just contests like every other
/// office), and no ballot-measure source was found for HI.
/// </summary>
[StateCode("HI")]
public sealed class HiCollector : IStateCollector
{
    private readonly HttpFetcher _fetcher;
    private readonly HiSourceConfig _config;
    private readonly IPublishSchedule _schedule;
    private readonly int _year;
    private readonly string _stateDataDir;

    public string StateCode => "HI";

    /// <param name="stateDataDir">Per-state output directory (data/output/&lt;xx&gt;/). Inputs are under data/input/&lt;xx&gt;/,
    /// including date_formats.json.</param>
    public HiCollector(HttpFetcher fetcher, int year, string stateDataDir, HiSourceConfig? config = null)
    {
        _fetcher = fetcher;
        _year = year;
        _stateDataDir = stateDataDir;
        _config = config ?? new HiSourceConfig();
        _schedule = new HiPublishSchedule();
    }

    public async Task<CollectResult> CollectAsync()
    {
        Console.WriteLine($"Collecting Hawaii ballot roster for {_year}...");

        var (dataRoot, _) = DataPaths.FromStateOutputDir(_stateDataDir);
        var dateFormats = DateFormatConfig.Load(DataPaths.DateFormatsPath(dataRoot, StateCode));
        var fieldMap = LookupTableLoader.Load(DataPaths.CandidateFieldMapPath(dataRoot, StateCode));

        var (primary, general) = await new ElectionDateScraper(_fetcher, _config, dateFormats).FetchAsync(_year);
        Console.WriteLine($"  {primary.Name} ({primary.ElectionDate:yyyy-MM-dd}), {general.Name} ({general.ElectionDate:yyyy-MM-dd})");
        RowHelpers.StampState([primary, general], StateCode);

        var (rows, pageUrl) = await new CandidateExportClient(_fetcher, _config).FetchAsync(_year);
        Console.WriteLine($"  {rows.Count} candidate rows exported from {pageUrl}");

        var result = new CollectResult();
        result.Elections.Add(primary);
        result.Elections.Add(general);

        foreach (var row in rows)
        {
            var election = HiCandidateMapper.DetermineElection(row.GetValueOrDefault("Status", ""), primary, general);
            var candidate = HiCandidateMapper.ToCandidateRow(row, election, pageUrl, fieldMap);
            RowHelpers.StampState(candidate, StateCode);
            result.Candidates.Add(candidate);
        }

        if (result.Candidates.Count == 0)
            throw new InvalidOperationException(
                "No candidates collected; refusing to write hollow outputs. Check https://olvr.hawaii.gov manually.");

        CollectResultSorter.Sort(result);
        BuildSourcesManifest(result, pageUrl);
        return result;
    }

    private void BuildSourcesManifest(CollectResult result, string pageUrl)
    {
        var sources = result.Sources;
        sources.Elections = [new SourceEntry(_config.ElectionsHomeUrl, "html")];
        sources.StatewideCandidates = [new SourceEntry(pageUrl, "csv")];
        sources.VerificationOnly = [new SourceEntry($"https://ballotpedia.org/Hawaii_elections,_{_year}", "html")];
        sources.NextRun = _schedule.Recommend(result, _year);
    }
}
