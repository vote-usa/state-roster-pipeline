using AngleSharp.Html.Parser;
using StateBallot.Core;
using StateBallot.Core.Raw;

namespace StateBallot.States.Sd;

/// <summary>
/// South Dakota state collector, backed by the SD SOS Voter Information
/// Portal (VIP). Both election ids are hand-maintained in
/// data/input/sd/election_ids.json - VIP has no discoverable index of them
/// (same limitation as NM's portal) - but unlike NM, the real dates for
/// *both* the primary and the general stay live and scrapable from the
/// evergreen election-calendar page even after the primary has passed, so
/// this state's v1 scope covers both elections, not just the general. The
/// candidate list page's "Export to CSV" button is a Telerik RadGrid toolbar
/// control wired to a client-side-only <c>__doPostBack</c>, not a genuine
/// submit button (see WebFormsPostback.TriggerPostbackAsync, added for this).
/// </summary>
[StateCode("SD")]
public sealed class SdCollector(int year, string stateDataDir, string? inputDataRoot = null, SdSourceConfig? config = null, SourceLinkSet? links = null)
    : StateCollectorBase<SdSourceConfig>(year, stateDataDir, inputDataRoot, config, links)
{
    protected override IPublishSchedule Schedule { get; } = new SdPublishSchedule();

    public const string UpcomingElectionsRole = "upcoming-elections";
    public const string CandidateCalendarRole = "candidate-calendar";
    public const string CandidateListPageRole = "candidate-list-page";
    public const string CandidateExportRole = "candidate-export";

    private static readonly string[] TargetTypes = ["Primary", "General"];

    public override async Task CaptureAsync(HttpFetcher fetcher, DateOnly asOf)
    {
        Console.WriteLine($"Capturing South Dakota sources for {Year}...");

        var indexHtml = await fetcher.GetStringAsync(Config.UpcomingElectionsPageUrl, FetchTag.Of(UpcomingElectionsRole));
        await fetcher.GetStringAsync(CalendarUrl(indexHtml), FetchTag.Of(CandidateCalendarRole));

        foreach (var type in TargetTypes)
        {
            if (Config.ElectionId(type) is not { } electionId)
                continue;

            var pageUrl = Config.CandidateListUrl(electionId);
            var pageHtml = await fetcher.GetStringAsync(pageUrl, FetchTag.Of(CandidateListPageRole, ("type", type)));
            await WebFormsPostback.TriggerPostbackAsync(
                fetcher, pageUrl, pageHtml, SdSelectors.ExportToCsvEventTarget, tag: FetchTag.Of(CandidateExportRole, ("type", type)));
        }
    }

    protected override CollectResult NormalizeCore(CaptureReader capture)
    {
        Console.WriteLine($"Normalizing South Dakota capture {capture.CaptureId} for {Year}...");


        var (calendarUrl, primaryDate, generalDate) = ParseElectionDates(capture, DateFormats);

        var result = new CollectResult();
        var candidateSourceUrls = new List<SourceEntry>();

        foreach (var (type, date) in new[] { ("Primary", primaryDate), ("General", generalDate) })
        {
            if (Config.ElectionId(type) is not { } electionId)
            {
                result.Gaps.Add(
                    $"No election-id/{type.ToLowerInvariant()} source parameter in {Config.Links.Origin}. " +
                    "VIP has no discoverable index of its own election ids - this must be re-derived by hand each cycle.");
                continue;
            }

            var election = SdCandidateMapper.ToElection(type, date, calendarUrl);
            RowHelpers.StampState(election, StateCode);
            result.Elections.Add(election);

            var pageUrl = Config.CandidateListUrl(electionId);
            var csvText = capture.Require(CandidateExportRole, ("type", type)).Text();
            // Strip the export's own UTF-8 BOM (U+FEFF), if the payload decoding left it in verbatim.
            csvText = csvText.TrimStart('﻿');

            var rows = DelimitedTableParser.Parse(csvText)
                .Where(r => !string.IsNullOrWhiteSpace(r.GetValueOrDefault("Contest")))
                .ToList();

            if (rows.Count == 0)
            {
                result.Gaps.Add($"{election.Name} ({date:yyyy-MM-dd}): no rows parsed from {pageUrl}. Re-run later.");
                result.PendingElections.Add(election);
                continue;
            }

            candidateSourceUrls.Add(new SourceEntry(pageUrl, "csv"));
            foreach (var row in rows)
            {
                var candidate = SdCandidateMapper.ToCandidateRow(row, election, pageUrl, FieldMap);
                RowHelpers.StampState(candidate, StateCode);
                result.Candidates.Add(candidate);
            }

            Console.WriteLine($"  {election.Name} ({date:yyyy-MM-dd}): {rows.Count} candidates ({pageUrl})");
        }

        BuildSourcesManifest(result, calendarUrl, candidateSourceUrls);
        return result;
    }

    /// <summary>
    /// The upcoming-elections index links to the current cycle's own
    /// "*-candidate-calendar.aspx" page, whose filename carries the year, so
    /// the link is followed instead of templating the URL.
    /// </summary>
    private string CalendarUrl(string indexHtml)
    {
        var linkMatch = SdSelectors.CandidateCalendarLink.Match(indexHtml);
        if (!linkMatch.Success)
            throw new InvalidOperationException($"No '*-candidate-calendar.aspx' link found on {Config.UpcomingElectionsPageUrl}.");
        return new Uri(new Uri(Config.UpcomingElectionsPageUrl), linkMatch.Groups["href"].Value).ToString();
    }

    private (string CalendarUrl, DateOnly Primary, DateOnly General) ParseElectionDates(CaptureReader capture, string[] dateFormats)
    {
        var calendarUrl = CalendarUrl(capture.Require(UpcomingElectionsRole).Text());

        var document = new HtmlParser().ParseDocument(capture.Require(CandidateCalendarRole).Text());
        var text = TextNormalization.CollapseWhitespace(document.Body?.TextContent ?? "");

        var primaryMatch = SdSelectors.PrimaryElectionDateLine.Match(text);
        var generalMatch = SdSelectors.GeneralElectionDateLine.Match(text);
        if (!primaryMatch.Success || !DateParsing.TryParseAny(primaryMatch.Groups["date"].Value, dateFormats, out var primaryDate))
            throw new InvalidOperationException($"No Primary election date parsed from {calendarUrl}.");
        if (!generalMatch.Success || !DateParsing.TryParseAny(generalMatch.Groups["date"].Value, dateFormats, out var generalDate))
            throw new InvalidOperationException($"No General election date parsed from {calendarUrl}.");
        if (primaryDate.Year != Year || generalDate.Year != Year)
            throw new InvalidOperationException(
                $"{calendarUrl} currently shows {primaryDate.Year}/{generalDate.Year} election dates, not {Year}. " +
                "The page may not have been updated for this year yet.");

        return (calendarUrl, primaryDate, generalDate);
    }

    private void BuildSourcesManifest(CollectResult result, string calendarUrl, List<SourceEntry> candidateListUrls)
    {
        var sources = result.Sources;
        sources.Elections = [new SourceEntry(calendarUrl, "html")];
        sources.StatewideCandidates = candidateListUrls;
        sources.VerificationOnly = Config.VerificationSources(Year);
    }
}
