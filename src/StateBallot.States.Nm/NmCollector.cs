using StateBallot.Core;

namespace StateBallot.States.Nm;

/// <summary>
/// New Mexico state collector, backed by the NM SOS's ASP.NET candidate
/// portal. v1 scope: the general election only. A primary candidate list
/// exists at the same portal, but this pipeline could find no live source for
/// its exact date once the primary itself has passed - the SOS site removes
/// per-election date/results pages once they're no longer current (confirmed:
/// several search-indexed date pages 404 today), unlike every other state
/// onboarded so far, and the candidate export itself never carries a date.
/// The portal also exposes no discoverable index of its own election ids (no
/// dropdown, no sibling links), so those are hand-maintained in
/// data/input/nm/election_ids.json rather than derived from a year - see
/// NmSourceConfig. The export itself is fetched as an HTML table dressed up
/// as an "Excel (xls)" download (see Core's HtmlTableParser) rather than the
/// same page's CSV option, which has a real data-quality bug: unescaped
/// commas in some fields shift every later column on that row.
/// </summary>
[StateCode("NM")]
public sealed class NmCollector : IStateCollector
{
    private readonly HttpFetcher _fetcher;
    private readonly NmSourceConfig _config;
    private readonly IPublishSchedule _schedule;
    private readonly int _year;
    private readonly string _stateDataDir;

    public string StateCode => "NM";

    /// <param name="stateDataDir">Per-state output directory (data/output/&lt;xx&gt;/). Inputs are under data/input/&lt;xx&gt;/,
    /// including date_formats.json and election_ids.json.</param>
    public NmCollector(HttpFetcher fetcher, int year, string stateDataDir, NmSourceConfig? config = null)
    {
        _fetcher = fetcher;
        _year = year;
        _stateDataDir = stateDataDir;
        _config = config ?? new NmSourceConfig();
        _schedule = new NmPublishSchedule();
    }

    public async Task<CollectResult> CollectAsync()
    {
        Console.WriteLine($"Collecting New Mexico ballot roster for {_year}...");

        var (dataRoot, _) = DataPaths.FromStateOutputDir(_stateDataDir);
        var dateFormats = DateFormatConfig.Load(DataPaths.DateFormatsPath(dataRoot, StateCode));
        var fieldMap = LookupTableLoader.Load(DataPaths.CandidateFieldMapPath(dataRoot, StateCode));
        var electionIds = LookupTableLoader.Load(DataPaths.ElectionIdsPath(dataRoot, StateCode));

        if (!electionIds.TryGetValue("General", out var electionId) || electionId.Length == 0)
            throw new InvalidOperationException(
                $"{DataPaths.ElectionIdsPath(dataRoot, StateCode)} has no \"General\" entry. NM's candidate " +
                "portal has no discoverable index of its own election ids - this must be re-derived by hand " +
                "each cycle (see NmSourceConfig's doc comment).");

        var electionsPageHtml = await _fetcher.GetStringAsync(_config.UpcomingElectionsPageUrl);
        var dateMatch = NmSelectors.GeneralElectionDateLine.Match(electionsPageHtml);
        if (!dateMatch.Success || !DateParsing.TryParseAny(dateMatch.Groups["date"].Value, dateFormats, out var electionDate))
            throw new InvalidOperationException(
                $"No General election date parsed from {_config.UpcomingElectionsPageUrl}.");
        if (electionDate.Year != _year)
            throw new InvalidOperationException(
                $"{_config.UpcomingElectionsPageUrl} currently shows a {electionDate.Year} date, not {_year}. " +
                "This page only ever lists the next upcoming election - back-filling a past year isn't supported by this source.");

        var election = NmCandidateMapper.ToElection("General", electionDate, _config.UpcomingElectionsPageUrl);
        RowHelpers.StampState(election, StateCode);
        Console.WriteLine("  Elections found: 1");

        var pageUrl = _config.CandidateListUrl(electionId);
        var pageHtml = await _fetcher.GetStringAsync(pageUrl);

        var exportFields = new Dictionary<string, string>
        {
            [NmSelectors.ExportFormatField] = NmSelectors.ExportFormatValue,
            [NmSelectors.PartyFilterField] = NmSelectors.AllPartiesOrCounties,
            [NmSelectors.CountyFilterField] = NmSelectors.AllPartiesOrCounties,
        };
        var exportHtml = await WebFormsPostback.ClickButtonAsync(
            _fetcher, pageUrl, pageHtml, NmSelectors.ExportButtonField, exportFields);
        var rows = HtmlTableParser.Parse(exportHtml);
        ScrapeGuard.RequireAny(rows, () => $"No rows parsed from the export at {pageUrl} (button '{NmSelectors.ExportButtonField}').");

        var result = new CollectResult();
        result.Elections.Add(election);

        foreach (var row in rows)
        {
            if (NmCandidateMapper.IsJudicialRetention(row))
            {
                var measure = NmRetentionMapper.ToMeasureRow(row, election, pageUrl);
                RowHelpers.StampState(measure, StateCode);
                result.StatewideProposedMeasures.Add(measure);
                continue;
            }

            var candidate = NmCandidateMapper.ToCandidateRow(row, election, pageUrl, fieldMap);
            RowHelpers.StampState(candidate, StateCode);
            result.Candidates.Add(candidate);
        }

        if (result.Candidates.Count == 0)
            throw new InvalidOperationException(
                $"No candidates collected from {pageUrl}; refusing to write hollow outputs. Check manually.");

        Console.WriteLine(
            $"  {election.Name} ({election.ElectionDate:yyyy-MM-dd}): {result.Candidates.Count} candidates, " +
            $"{result.StatewideProposedMeasures.Count} judicial retention questions ({pageUrl})");

        CollectResultSorter.Sort(result);
        BuildSourcesManifest(result, pageUrl);
        return result;
    }

    private void BuildSourcesManifest(CollectResult result, string candidateListUrl)
    {
        var sources = result.Sources;
        sources.Elections = [new SourceEntry(_config.UpcomingElectionsPageUrl, "html")];
        sources.StatewideCandidates = [new SourceEntry(candidateListUrl, "html")];
        sources.StatewideMeasures = [new SourceEntry(candidateListUrl, "html")];
        sources.VerificationOnly = [new SourceEntry($"https://ballotpedia.org/New_Mexico_elections,_{_year}", "html")];
        sources.NextRun = _schedule.Recommend(result, _year);
    }
}
