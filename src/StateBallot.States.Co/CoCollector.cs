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
public sealed class CoCollector(int year, string stateDataDir, string? inputDataRoot = null, CoSourceConfig? config = null, SourceLinkSet? links = null)
    : StateCollectorBase<CoSourceConfig>(year, stateDataDir, inputDataRoot, config, links)
{
    protected override IPublishSchedule Schedule { get; } = new CoPublishSchedule();

    public const string CandidateListPageRole = "candidate-list-page";
    public const string CandidateListRole = "candidate-list";

    public override async Task CaptureAsync(HttpFetcher fetcher, DateOnly asOf)
    {
        Console.WriteLine($"Capturing Colorado sources for {Year}...");

        var elections = await new CoElectionDateScraper(Config, DateFormats).TryCaptureAsync(fetcher, Year);
        if (elections is null)
            return;

        foreach (var type in elections.Select(e => e.ElectionType).Distinct())
        {
            var pageUrl = Config.CandidateListPageUrl(type);
            var html = await fetcher.GetStringAsync(pageUrl, FetchTag.Of(CandidateListPageRole, ("type", type)));
            var (xlsxUrl, _) = ResolveXlsxUrl(pageUrl, html);
            if (xlsxUrl is not null)
                await fetcher.GetBytesAsync(xlsxUrl, FetchTag.Of(CandidateListRole, ("type", type)));
        }
    }

    protected override CollectResult NormalizeCore(CaptureReader capture)
    {
        Console.WriteLine($"Normalizing Colorado capture {capture.CaptureId} for {Year}...");

        var calendarUrl = Config.ElectionCalendarPdfUrl(Year);
        var calendar = capture.Require(CoElectionDateScraper.Role);
        if (!calendar.HasPayload)
            throw new InvalidOperationException(
                $"No election calendar published yet for {Year} at {calendarUrl}. Re-run later.");
        var elections = new CoElectionDateScraper(Config, DateFormats).Parse(calendar.Bytes(), Year);
        Console.WriteLine($"  Elections found: {elections.Count}");
        RowHelpers.StampState(elections, StateCode);

        var result = new CollectResult();
        result.Elections.AddRange(elections);
        var candidateListUrls = new List<SourceEntry>();

        foreach (var election in elections)
        {
            var pageUrl = Config.CandidateListPageUrl(election.ElectionType);
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
                var candidate = CoCandidateMapper.ToCandidateRow(row, election, xlsxUrl, FieldMap);
                RowHelpers.StampState(candidate, StateCode);
                result.Candidates.Add(candidate);
            }

            Console.WriteLine($"  {election.Name} ({election.ElectionDate:yyyy-MM-dd}): {rows.Count} candidates ({xlsxUrl})");
        }

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
        if (!headingMatch.Success || headingMatch.Groups["year"].Value != Year.ToString())
            return (null,
                $"{pageUrl} does not show a {Year} " +
                $"candidate list (found heading: '{(headingMatch.Success ? headingMatch.Value : "none")}'). " +
                "This page always reflects the current cycle only; back-filling past years isn't supported by this source.");

        var href = doc.QuerySelector(CoSelectors.XlsxLinkCss)?.GetAttribute("href");
        if (string.IsNullOrWhiteSpace(href))
            return (null, $"no XLSX link found on {pageUrl}.");

        return (Config.Resolve(pageUrl, href), null);
    }

    private void BuildSourcesManifest(CollectResult result, string calendarUrl, List<SourceEntry> candidateListUrls)
    {
        var sources = result.Sources;
        sources.Elections = [new SourceEntry(calendarUrl, "pdf")];
        sources.StatewideCandidates = candidateListUrls;
        sources.VerificationOnly = Config.VerificationSources(Year);
    }
}
