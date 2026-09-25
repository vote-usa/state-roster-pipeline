using AngleSharp.Html.Parser;
using StateBallot.Core;

namespace StateBallot.States.Co;

/// <summary>
/// Colorado state collector, backed by the CO SOS elections site. The
/// Primary/General candidate lists are separate static XLSX files linked off
/// two always-current-cycle HTML pages (no bot-blocking, no session/auth) -
/// neither the page nor the file is year-parameterized, so this cross-checks
/// each page's own "20NN &lt;type&gt; Election ... Candidate List" heading
/// against the requested year before trusting its data (see CoSourceConfig).
/// Neither carries the election's actual date - that's scraped separately
/// from the statutory election calendar PDF (see CoElectionDateScraper). v1
/// scope: candidates only (name/office/district/party) - CO's export has no
/// filing date, address, or contact info at all.
/// </summary>
[StateCode("CO")]
public sealed class CoCollector : IStateCollector
{
    private readonly HttpFetcher _fetcher;
    private readonly CoSourceConfig _config;
    private readonly IPublishSchedule _schedule;
    private readonly int _year;
    private readonly string _stateDataDir;
    private readonly string _inputDataRoot;

    public string StateCode => "CO";

    /// <param name="stateDataDir">Per-state output directory (data/output/&lt;xx&gt;/). Inputs are under data/input/&lt;xx&gt;/,
    /// including date_formats.json.</param>
    public CoCollector(
        HttpFetcher fetcher, int year, string stateDataDir, string? inputDataRoot = null, CoSourceConfig? config = null)
    {
        _fetcher = fetcher;
        _year = year;
        _stateDataDir = stateDataDir;
        _inputDataRoot = ResolveInputDataRoot(stateDataDir, inputDataRoot);
        _config = config ?? new CoSourceConfig();
        _schedule = new CoPublishSchedule();
    }

    public async Task<CollectResult> CollectAsync()
    {
        Console.WriteLine($"Collecting Colorado ballot roster for {_year}...");

        var dataRoot = _inputDataRoot;
        var dateFormats = DateFormatConfig.Load(DataPaths.DateFormatsPath(dataRoot, StateCode));
        var fieldMap = LookupTableLoader.Load(DataPaths.CandidateFieldMapPath(dataRoot, StateCode));

        var calendarUrl = _config.ElectionCalendarPdfUrl(_year);
        var elections = await new CoElectionDateScraper(_fetcher, _config, dateFormats).TryFetchAsync(_year)
            ?? throw new InvalidOperationException(
                $"No election calendar published yet for {_year} at {calendarUrl}. Re-run later.");
        Console.WriteLine($"  Elections found: {elections.Count}");
        RowHelpers.StampState(elections, StateCode);

        var result = new CollectResult();
        result.Elections.AddRange(elections);
        var candidateListUrls = new List<SourceEntry>();

        foreach (var election in elections)
        {
            var pageUrl = _config.CandidateListPageUrl(election.ElectionType);
            var xlsxUrl = await TryResolveXlsxUrlAsync(pageUrl, election, result);
            if (xlsxUrl is null)
                continue;

            candidateListUrls.Add(new SourceEntry(xlsxUrl, "xlsx"));
            var bytes = await _fetcher.GetBytesAsync(xlsxUrl);
            // Both files end with a literal "End of Data"/"End of data" sentinel
            // row (blank Office/District/Party) - not a real candidate. Every
            // genuine row has a non-blank Office, so that's the filter.
            var rows = XlsxTableParser.Parse(bytes)
                .Where(r => !string.IsNullOrWhiteSpace(r.GetValueOrDefault("Office")))
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
                var candidate = CoCandidateMapper.ToCandidateRow(row, election, xlsxUrl, fieldMap);
                RowHelpers.StampState(candidate, StateCode);
                result.Candidates.Add(candidate);
            }

            Console.WriteLine($"  {election.Name} ({election.ElectionDate:yyyy-MM-dd}): {rows.Count} candidates ({xlsxUrl})");
        }

        if (result.Candidates.Count == 0)
            throw new InvalidOperationException(
                "No candidates collected for any discovered election; refusing to write hollow outputs. " +
                "Check https://www.sos.state.co.us/pubs/elections/Candidates/CandidateHome.html manually.");

        CollectResultSorter.Sort(result);
        BuildSourcesManifest(result, calendarUrl, candidateListUrls);
        return result;
    }

    /// <summary>
    /// Fetches a candidate list page, verifies its own heading names the
    /// requested year (it's the same URL for every cycle), and returns the
    /// linked XLSX's absolute URL - or null (recording a Gap) if the page
    /// doesn't match, has no XLSX link, or isn't reachable yet.
    /// </summary>
    private async Task<string?> TryResolveXlsxUrlAsync(string pageUrl, Election election, CollectResult result)
    {
        var html = await _fetcher.GetStringAsync(pageUrl);
        var doc = await new HtmlParser().ParseDocumentAsync(html);

        var headingMatch = CoSelectors.CandidateListHeading.Match(doc.Body?.TextContent ?? "");
        if (!headingMatch.Success || headingMatch.Groups["year"].Value != _year.ToString())
        {
            result.Gaps.Add(
                $"{election.Name} ({election.ElectionDate:yyyy-MM-dd}): {pageUrl} does not show a {_year} " +
                $"candidate list (found heading: '{(headingMatch.Success ? headingMatch.Value : "none")}'). " +
                "This page always reflects the current cycle only; back-filling past years isn't supported by this source.");
            result.PendingElections.Add(election);
            return null;
        }

        var link = doc.QuerySelector(CoSelectors.XlsxLinkCss);
        var href = link?.GetAttribute("href");
        if (string.IsNullOrWhiteSpace(href))
        {
            result.Gaps.Add($"{election.Name} ({election.ElectionDate:yyyy-MM-dd}): no XLSX link found on {pageUrl}.");
            result.PendingElections.Add(election);
            return null;
        }

        return _config.Resolve(pageUrl, href);
    }

    private void BuildSourcesManifest(CollectResult result, string calendarUrl, List<SourceEntry> candidateListUrls)
    {
        var sources = result.Sources;
        sources.Elections = [new SourceEntry(calendarUrl, "pdf")];
        sources.StatewideCandidates = candidateListUrls;
        sources.VerificationOnly = [new SourceEntry($"https://ballotpedia.org/Colorado_elections,_{_year}", "html")];
        sources.NextRun = _schedule.Recommend(result, _year);
    }

    private static string ResolveInputDataRoot(string stateDataDir, string? inputDataRoot)
    {
        if (!string.IsNullOrWhiteSpace(inputDataRoot))
            return Path.GetFullPath(inputDataRoot);
        return DataPaths.TryInferPipelineDataRoot(stateDataDir)
            ?? throw new InvalidOperationException(
                $"Cannot infer input data root from output dir '{stateDataDir}'. " +
                "Pass inputDataRoot (CLI --input-root) when writing outside data/output/<xx>.");
    }
}
