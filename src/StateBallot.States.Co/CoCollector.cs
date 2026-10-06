using AngleSharp.Html.Parser;
using StateBallot.Core;
using StateBallot.Core.Raw;

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
    private readonly CoSourceConfig _config;
    private readonly IPublishSchedule _schedule;
    private readonly int _year;
    private readonly string _stateDataDir;
    private readonly string _inputDataRoot;

    public string StateCode => "CO";

    /// <param name="stateDataDir">Per-state output directory (data/output/&lt;xx&gt;/). Inputs are under data/input/&lt;xx&gt;/,
    /// including date_formats.json.</param>
    public CoCollector(
        int year, string stateDataDir, string? inputDataRoot = null, CoSourceConfig? config = null)
    {
        _year = year;
        _stateDataDir = stateDataDir;
        _inputDataRoot = ResolveInputDataRoot(stateDataDir, inputDataRoot);
        _config = config ?? new CoSourceConfig();
        _schedule = new CoPublishSchedule();
    }

    public const string CandidateListPageRole = "candidate-list-page";
    public const string CandidateListRole = "candidate-list";

    public async Task CaptureAsync(HttpFetcher fetcher, DateOnly asOf)
    {
        Console.WriteLine($"Capturing Colorado sources for {_year}...");

        var dateFormats = DateFormatConfig.Load(DataPaths.DateFormatsPath(_inputDataRoot, StateCode));
        var elections = await new CoElectionDateScraper(_config, dateFormats).TryCaptureAsync(fetcher, _year);
        if (elections is null)
            return;

        foreach (var type in elections.Select(e => e.ElectionType).Distinct())
        {
            var pageUrl = _config.CandidateListPageUrl(type);
            var html = await fetcher.GetStringAsync(pageUrl, FetchTag.Of(CandidateListPageRole, ("type", type)));
            var (xlsxUrl, _) = ResolveXlsxUrl(pageUrl, html);
            if (xlsxUrl is not null)
                await fetcher.GetBytesAsync(xlsxUrl, FetchTag.Of(CandidateListRole, ("type", type)));
        }
    }

    public CollectResult Normalize(CaptureReader capture)
    {
        Console.WriteLine($"Normalizing Colorado capture {capture.CaptureId} for {_year}...");

        var dataRoot = _inputDataRoot;
        var dateFormats = DateFormatConfig.Load(DataPaths.DateFormatsPath(dataRoot, StateCode));
        var fieldMap = LookupTableLoader.Load(DataPaths.CandidateFieldMapPath(dataRoot, StateCode));

        var calendarUrl = _config.ElectionCalendarPdfUrl(_year);
        var calendar = capture.Require(CoElectionDateScraper.Role);
        if (!calendar.HasPayload)
            throw new InvalidOperationException(
                $"No election calendar published yet for {_year} at {calendarUrl}. Re-run later.");
        var elections = new CoElectionDateScraper(_config, dateFormats).Parse(calendar.Bytes(), _year);
        Console.WriteLine($"  Elections found: {elections.Count}");
        RowHelpers.StampState(elections, StateCode);

        var result = new CollectResult();
        result.Elections.AddRange(elections);
        var candidateListUrls = new List<SourceEntry>();

        foreach (var election in elections)
        {
            var pageUrl = _config.CandidateListPageUrl(election.ElectionType);
            var pageHtml = capture.Require(CandidateListPageRole, ("type", election.ElectionType)).Text();
            var (xlsxUrl, problem) = ResolveXlsxUrl(pageUrl, pageHtml);
            if (xlsxUrl is null)
            {
                result.Gaps.Add($"{election.Name} ({election.ElectionDate:yyyy-MM-dd}): {problem}");
                result.PendingElections.Add(election);
                continue;
            }

            candidateListUrls.Add(new SourceEntry(xlsxUrl, "xlsx"));
            var bytes = capture.Require(CandidateListRole, ("type", election.ElectionType)).Bytes();
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
    /// Verifies a candidate list page's own heading names the requested year
    /// (it's the same URL for every cycle) and returns the linked XLSX's
    /// absolute URL, or null with the reason when the page doesn't match or
    /// has no XLSX link.
    /// </summary>
    private (string? XlsxUrl, string? Problem) ResolveXlsxUrl(string pageUrl, string html)
    {
        var doc = new HtmlParser().ParseDocument(html);

        var headingMatch = CoSelectors.CandidateListHeading.Match(doc.Body?.TextContent ?? "");
        if (!headingMatch.Success || headingMatch.Groups["year"].Value != _year.ToString())
            return (null,
                $"{pageUrl} does not show a {_year} " +
                $"candidate list (found heading: '{(headingMatch.Success ? headingMatch.Value : "none")}'). " +
                "This page always reflects the current cycle only; back-filling past years isn't supported by this source.");

        var href = doc.QuerySelector(CoSelectors.XlsxLinkCss)?.GetAttribute("href");
        if (string.IsNullOrWhiteSpace(href))
            return (null, $"no XLSX link found on {pageUrl}.");

        return (_config.Resolve(pageUrl, href), null);
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
