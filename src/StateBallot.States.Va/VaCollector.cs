using AngleSharp.Html.Parser;
using StateBallot.Core;
using StateBallot.Core.Raw;

namespace StateBallot.States.Va;

/// <summary>
/// Virginia state collector, backed by the VA Dept. of Elections site. v1
/// scope: the general election's "All Offices" candidate list only - a
/// primary, when VA runs one, is published as separate per-party (Democratic/
/// Republican) and per-scope (federal/local) XLSX files with ad hoc,
/// hand-typed slugs and no consistent naming across cycles, unlike the single
/// general-election "All Offices" list; not attempted here. Neither the
/// index page nor any individual election page/XLSX filename is
/// year-templatable (filenames even carry ad hoc revision-date suffixes), so
/// everything is discovered by scraping the evergreen index page for the
/// current cycle's "All Offices" link, then that page for its own election
/// date (from its &lt;title&gt;) and linked XLSX - two HTML fetches before the
/// XLSX itself. The XLSX repeats every federal/statewide race once per
/// covered Virginia locality (of which there are ~133); VaCandidateMapper
/// collapses that back to one row per real candidacy.
/// </summary>
[StateCode("VA")]
public sealed class VaCollector(int year, string stateDataDir, string? inputDataRoot = null, VaSourceConfig? config = null, SourceLinkSet? links = null)
    : StateCollectorBase<VaSourceConfig>(year, stateDataDir, inputDataRoot, config, links)
{
    protected override IPublishSchedule Schedule { get; } = new VaPublishSchedule();

    public const string CandidateListIndexRole = "candidate-list-index";
    public const string ElectionPageRole = "election-page";
    public const string CandidateListRole = "candidate-list";

    public override async Task CaptureAsync(HttpFetcher fetcher, DateOnly asOf)
    {
        Console.WriteLine($"Capturing Virginia sources for {Year}...");

        var electionPageUrl = FindElectionPageUrl(
            await fetcher.GetStringAsync(Config.CandidateListIndexUrl, FetchTag.Of(CandidateListIndexRole)));
        var pageHtml = await fetcher.GetStringAsync(electionPageUrl, FetchTag.Of(ElectionPageRole));
        await fetcher.GetBytesAsync(
            XlsxUrl(electionPageUrl, new HtmlParser().ParseDocument(pageHtml)), FetchTag.Of(CandidateListRole));
    }

    protected override CollectResult NormalizeCore(CaptureReader capture)
    {
        Console.WriteLine($"Normalizing Virginia capture {capture.CaptureId} for {Year}...");

        var electionPageUrl = FindElectionPageUrl(capture.Require(CandidateListIndexRole).Text());
        var electionPage = new HtmlParser().ParseDocument(capture.Require(ElectionPageRole).Text());

        var titleMatch = VaSelectors.TitleDate.Match(electionPage.Title ?? "");
        if (!titleMatch.Success || !DateParsing.TryParseAny(titleMatch.Groups["date"].Value, DateFormats, out var electionDate))
            throw new InvalidOperationException($"No election date parsed from {electionPageUrl}'s <title>.");
        if (electionDate.Year != Year)
            throw new InvalidOperationException(
                $"{electionPageUrl} shows a {electionDate.Year} date, not {Year}. The index page may not have been updated for this year yet.");

        var election = VaCandidateMapper.ToElection("General", electionDate, electionPageUrl);
        RowHelpers.StampState(election, StateCode);
        Console.WriteLine($"  Elections found: 1");

        var xlsxUrl = XlsxUrl(electionPageUrl, electionPage);

        var result = new CollectResult();
        result.Elections.Add(election);

        var bytes = capture.Require(CandidateListRole).Bytes();
        var rawRows = XlsxTableParser.Parse(bytes)
            .Where(r => !string.IsNullOrWhiteSpace(r.GetValueOrDefault("Office Title")))
            .Select(row => VaCandidateMapper.ToCandidateRow(row, election, xlsxUrl, FieldMap))
            .ToList();
        var candidates = VaCandidateMapper.MergeDuplicateLocalities(rawRows);

        if (candidates.Count == 0)
            throw new InvalidOperationException(
                $"No candidates parsed from {xlsxUrl}; refusing to write hollow outputs. Check {electionPageUrl} manually.");

        foreach (var candidate in candidates)
        {
            RowHelpers.StampState(candidate, StateCode);
            result.Candidates.Add(candidate);
        }

        Console.WriteLine($"  {election.Name} ({election.ElectionDate:yyyy-MM-dd}): {candidates.Count} candidates " +
            $"({rawRows.Count} rows before locality dedup) ({xlsxUrl})");

        BuildSourcesManifest(result, electionPageUrl, xlsxUrl);
        return result;
    }

    /// <summary>
    /// The index page links one "&lt;year&gt; ... All Offices Candidate List"
    /// page per current-cycle election. The link is followed, not templated,
    /// because the page's own URL carries no predictable year or date.
    /// </summary>
    private string FindElectionPageUrl(string indexHtml)
    {
        var indexDoc = new HtmlParser().ParseDocument(indexHtml);

        foreach (var link in indexDoc.QuerySelectorAll("a"))
        {
            var text = TextNormalization.CollapseWhitespace(link.TextContent);
            var match = VaSelectors.AllOfficesLinkText.Match(text);
            if (!match.Success || match.Groups["year"].Value != Year.ToString())
                continue;

            var href = link.GetAttribute("href");
            if (string.IsNullOrWhiteSpace(href))
                continue;

            return new Uri(new Uri(Config.CandidateListIndexUrl), href).ToString();
        }

        throw new InvalidOperationException(
            $"{Config.CandidateListIndexUrl} has no '{Year} ... All Offices Candidate List' link. " +
            "This index only ever shows the current cycle - back-filling a past year isn't supported by this source.");
    }

    private static string XlsxUrl(string electionPageUrl, AngleSharp.Dom.IDocument electionPage)
    {
        var xlsxHref = electionPage.QuerySelector(VaSelectors.XlsxLinkCss)?.GetAttribute("href")
            ?? throw new InvalidOperationException($"No XLSX link found on {electionPageUrl}.");
        return new Uri(new Uri(electionPageUrl), xlsxHref).ToString();
    }

    private void BuildSourcesManifest(CollectResult result, string electionPageUrl, string xlsxUrl)
    {
        var sources = result.Sources;
        sources.Elections = [new SourceEntry(electionPageUrl, "html")];
        sources.StatewideCandidates = [new SourceEntry(xlsxUrl, "xlsx")];
        sources.VerificationOnly = Config.VerificationSources(Year);
    }
}
