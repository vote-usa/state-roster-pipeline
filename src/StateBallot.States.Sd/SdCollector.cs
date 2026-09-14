using AngleSharp.Html.Parser;
using StateBallot.Core;

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
public sealed class SdCollector : IStateCollector
{
    private readonly HttpFetcher _fetcher;
    private readonly SdSourceConfig _config;
    private readonly IPublishSchedule _schedule;
    private readonly int _year;
    private readonly string _stateDataDir;

    public string StateCode => "SD";

    /// <param name="stateDataDir">Per-state output directory (data/output/&lt;xx&gt;/). Inputs are under data/input/&lt;xx&gt;/,
    /// including date_formats.json and election_ids.json.</param>
    public SdCollector(HttpFetcher fetcher, int year, string stateDataDir, SdSourceConfig? config = null)
    {
        _fetcher = fetcher;
        _year = year;
        _stateDataDir = stateDataDir;
        _config = config ?? new SdSourceConfig();
        _schedule = new SdPublishSchedule();
    }

    public async Task<CollectResult> CollectAsync()
    {
        Console.WriteLine($"Collecting South Dakota ballot roster for {_year}...");

        var (dataRoot, _) = DataPaths.FromStateOutputDir(_stateDataDir);
        var dateFormats = DateFormatConfig.Load(DataPaths.DateFormatsPath(dataRoot, StateCode));
        var fieldMap = LookupTableLoader.Load(DataPaths.CandidateFieldMapPath(dataRoot, StateCode));
        var electionIds = LookupTableLoader.Load(DataPaths.ElectionIdsPath(dataRoot, StateCode));

        var (calendarUrl, primaryDate, generalDate) = await FetchElectionDatesAsync(dateFormats);

        var result = new CollectResult();
        var candidateSourceUrls = new List<SourceEntry>();

        foreach (var (type, date) in new[] { ("Primary", primaryDate), ("General", generalDate) })
        {
            if (!electionIds.TryGetValue(type, out var electionId) || electionId.Length == 0)
            {
                result.Gaps.Add(
                    $"{DataPaths.ElectionIdsPath(dataRoot, StateCode)} has no \"{type}\" entry. " +
                    "VIP has no discoverable index of its own election ids - this must be re-derived by hand each cycle.");
                continue;
            }

            var election = SdCandidateMapper.ToElection(type, date, calendarUrl);
            RowHelpers.StampState(election, StateCode);
            result.Elections.Add(election);

            var pageUrl = _config.CandidateListUrl(electionId);
            var pageHtml = await _fetcher.GetStringAsync(pageUrl);
            var csvText = await WebFormsPostback.TriggerPostbackAsync(_fetcher, pageUrl, pageHtml, SdSelectors.ExportToCsvEventTarget);
            // Strip the export's own UTF-8 BOM (U+FEFF), if HttpFetcher's string decoding left it in verbatim.
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
                var candidate = SdCandidateMapper.ToCandidateRow(row, election, pageUrl, fieldMap);
                RowHelpers.StampState(candidate, StateCode);
                result.Candidates.Add(candidate);
            }

            Console.WriteLine($"  {election.Name} ({date:yyyy-MM-dd}): {rows.Count} candidates ({pageUrl})");
        }

        if (result.Candidates.Count == 0)
            throw new InvalidOperationException(
                "No candidates collected for any discovered election; refusing to write hollow outputs. " +
                "Check https://vip.sdsos.gov manually.");

        CollectResultSorter.Sort(result);
        BuildSourcesManifest(result, calendarUrl, candidateSourceUrls);
        return result;
    }

    /// <summary>Follows the evergreen upcoming-elections page to its current "*-candidate-calendar.aspx" link, and scrapes both election dates from it.</summary>
    private async Task<(string CalendarUrl, DateOnly Primary, DateOnly General)> FetchElectionDatesAsync(string[] dateFormats)
    {
        var indexHtml = await _fetcher.GetStringAsync(_config.UpcomingElectionsPageUrl);
        var linkMatch = SdSelectors.CandidateCalendarLink.Match(indexHtml);
        if (!linkMatch.Success)
            throw new InvalidOperationException($"No '*-candidate-calendar.aspx' link found on {_config.UpcomingElectionsPageUrl}.");
        var calendarUrl = new Uri(new Uri(_config.UpcomingElectionsPageUrl), linkMatch.Groups["href"].Value).ToString();

        var calendarHtml = await _fetcher.GetStringAsync(calendarUrl);
        var document = new HtmlParser().ParseDocument(calendarHtml);
        var text = TextNormalization.CollapseWhitespace(document.Body?.TextContent ?? "");

        var primaryMatch = SdSelectors.PrimaryElectionDateLine.Match(text);
        var generalMatch = SdSelectors.GeneralElectionDateLine.Match(text);
        if (!primaryMatch.Success || !DateParsing.TryParseAny(primaryMatch.Groups["date"].Value, dateFormats, out var primaryDate))
            throw new InvalidOperationException($"No Primary election date parsed from {calendarUrl}.");
        if (!generalMatch.Success || !DateParsing.TryParseAny(generalMatch.Groups["date"].Value, dateFormats, out var generalDate))
            throw new InvalidOperationException($"No General election date parsed from {calendarUrl}.");
        if (primaryDate.Year != _year || generalDate.Year != _year)
            throw new InvalidOperationException(
                $"{calendarUrl} currently shows {primaryDate.Year}/{generalDate.Year} election dates, not {_year}. " +
                "The page may not have been updated for this year yet.");

        return (calendarUrl, primaryDate, generalDate);
    }

    private void BuildSourcesManifest(CollectResult result, string calendarUrl, List<SourceEntry> candidateListUrls)
    {
        var sources = result.Sources;
        sources.Elections = [new SourceEntry(calendarUrl, "html")];
        sources.StatewideCandidates = candidateListUrls;
        sources.VerificationOnly = [new SourceEntry($"https://ballotpedia.org/South_Dakota_elections,_{_year}", "html")];
        sources.NextRun = _schedule.Recommend(result, _year);
    }
}
