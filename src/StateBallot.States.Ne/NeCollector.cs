using StateBallot.Core;

namespace StateBallot.States.Ne;

/// <summary>
/// Nebraska state collector, backed by the SOS's single statewide filing
/// workbook (XLSX) plus the elections page's own plain-text Primary/General
/// dates (neither workbook sheet carries a date column). Sheet 0 covers every
/// partisan and nonpartisan race in one file - federal, state, legislative,
/// education/regents/community-college boards, and dozens of local special
/// districts (public power, natural resources, reclamation) - none of which
/// carry a county. Sheet 1 is judicial retention questions, mapped to
/// StatewideProposedMeasures (see NeRetentionMapper). v1 scope: no county
/// directory or local/county-administered ballot measures (NE's aren't
/// published in this workbook).
/// </summary>
[StateCode("NE")]
public sealed class NeCollector : IStateCollector
{
    private readonly HttpFetcher _fetcher;
    private readonly NeSourceConfig _config;
    private readonly IPublishSchedule _schedule;
    private readonly int _year;
    private readonly string _stateDataDir;

    public string StateCode => "NE";

    /// <param name="stateDataDir">Per-state output directory (data/output/&lt;xx&gt;/). Inputs are under data/input/&lt;xx&gt;/,
    /// including date_formats.json.</param>
    public NeCollector(HttpFetcher fetcher, int year, string stateDataDir, NeSourceConfig? config = null)
    {
        _fetcher = fetcher;
        _year = year;
        _stateDataDir = stateDataDir;
        _config = config ?? new NeSourceConfig();
        _schedule = new NePublishSchedule();
    }

    public async Task<CollectResult> CollectAsync()
    {
        Console.WriteLine($"Collecting Nebraska ballot roster for {_year}...");

        var (dataRoot, _) = DataPaths.FromStateOutputDir(_stateDataDir);
        var dateFormats = DateFormatConfig.Load(DataPaths.DateFormatsPath(dataRoot, StateCode));
        var fieldMap = LookupTableLoader.Load(DataPaths.CandidateFieldMapPath(dataRoot, StateCode));

        var (primary, general) = await new ElectionDateScraper(_fetcher, _config, dateFormats).FetchAsync(_year);
        Console.WriteLine($"  {primary.Name} ({primary.ElectionDate:yyyy-MM-dd}), {general.Name} ({general.ElectionDate:yyyy-MM-dd})");
        RowHelpers.StampState([primary, general], StateCode);

        var url = _config.StatewideCandidateFilingListUrl(_year);
        var xlsxBytes = await _fetcher.GetBytesAsync(url);

        // The workbook is a live "currently filed" snapshot, not a fixed
        // as-of-filing-deadline list (same idea as MS's CSV export): before the
        // primary it lists every filed candidate per partisan office (a
        // contested field, multiple per party); after the primary it's already
        // narrowed to the resolved nominees. There's no per-row date to key off
        // like MS has, so the whole sheet is attributed to whichever election
        // is next based on today vs. the primary's own date - a known
        // simplification for local nonpartisan races that never have a primary
        // at all (they're General-bound regardless of this comparison, so nothing
        // is actually miscategorized for those).
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var candidateElection = today > primary.ElectionDate ? general : primary;

        var candidateRows = XlsxTableParser.Parse(xlsxBytes)
            .Where(r => r.GetValueOrDefault("Candidate Name", "").Trim().Length > 0) // drop trailing blank filler rows
            .ToList();
        ScrapeGuard.RequireAny(candidateRows, () => $"No candidate rows parsed from {url}.");

        var retentionRows = XlsxTableParser.Parse(xlsxBytes, sheetIndex: 1)
            .Where(r => r.GetValueOrDefault("Judge (Ballot Name)", "").Trim().Length > 0)
            .ToList();

        var result = new CollectResult();
        result.Elections.Add(primary);
        result.Elections.Add(general);

        foreach (var row in candidateRows)
        {
            var candidate = NeCandidateMapper.ToCandidateRow(row, candidateElection, url, fieldMap);
            RowHelpers.StampState(candidate, StateCode);
            result.Candidates.Add(candidate);
        }
        Console.WriteLine($"  {candidateElection.Name}: {result.Candidates.Count} candidates ({url})");

        foreach (var row in retentionRows)
        {
            var measure = NeRetentionMapper.ToMeasureRow(row, general, url);
            RowHelpers.StampState(measure, StateCode);
            result.StatewideProposedMeasures.Add(measure);
        }
        Console.WriteLine($"  {general.Name}: {result.StatewideProposedMeasures.Count} judicial retention questions ({url})");

        if (result.Candidates.Count == 0)
            throw new InvalidOperationException(
                "No candidates collected; refusing to write hollow outputs. Check https://sos.nebraska.gov/elections manually.");

        CollectResultSorter.Sort(result);
        BuildSourcesManifest(result, url);
        return result;
    }

    private void BuildSourcesManifest(CollectResult result, string candidateListUrl)
    {
        var sources = result.Sources;
        sources.Elections = [new SourceEntry(_config.ElectionsPageUrl, "html")];
        sources.StatewideCandidates = [new SourceEntry(candidateListUrl, "xlsx")];
        sources.StatewideMeasures = [new SourceEntry(candidateListUrl, "xlsx")];
        sources.VerificationOnly = [new SourceEntry($"https://ballotpedia.org/Nebraska_elections,_{_year}", "html")];
        sources.NextRun = _schedule.Recommend(result, _year);
    }
}
