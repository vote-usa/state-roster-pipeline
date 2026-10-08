using StateBallot.Core;
using StateBallot.Core.Raw;

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
public sealed class NmCollector(int year, string stateDataDir, string? inputDataRoot = null, NmSourceConfig? config = null, SourceLinkSet? links = null)
    : StateCollectorBase<NmSourceConfig>(year, stateDataDir, inputDataRoot, config, links)
{
    protected override IPublishSchedule Schedule { get; } = new NmPublishSchedule();

    public const string UpcomingElectionsRole = "upcoming-elections";
    public const string CandidateListPageRole = "candidate-list-page";
    public const string CandidateExportRole = "candidate-export";

    public override async Task CaptureAsync(HttpFetcher fetcher, DateOnly asOf)
    {
        Console.WriteLine($"Capturing New Mexico sources for {Year}...");

        var pageUrl = Config.CandidateListUrl(RequireGeneralElectionId());
        await fetcher.GetStringAsync(Config.UpcomingElectionsPageUrl, FetchTag.Of(UpcomingElectionsRole));
        var pageHtml = await fetcher.GetStringAsync(pageUrl, FetchTag.Of(CandidateListPageRole));

        var exportFields = new Dictionary<string, string>
        {
            [NmSelectors.ExportFormatField] = NmSelectors.ExportFormatValue,
            [NmSelectors.PartyFilterField] = NmSelectors.AllPartiesOrCounties,
            [NmSelectors.CountyFilterField] = NmSelectors.AllPartiesOrCounties,
        };
        await WebFormsPostback.ClickButtonAsync(
            fetcher, pageUrl, pageHtml, NmSelectors.ExportButtonField, exportFields, FetchTag.Of(CandidateExportRole));
    }

    protected override CollectResult NormalizeCore(CaptureReader capture)
    {
        Console.WriteLine($"Normalizing New Mexico capture {capture.CaptureId} for {Year}...");

        var electionId = RequireGeneralElectionId();

        var electionsPageHtml = capture.Require(UpcomingElectionsRole).Text();
        var dateMatch = NmSelectors.GeneralElectionDateLine.Match(electionsPageHtml);
        if (!dateMatch.Success || !DateParsing.TryParseAny(dateMatch.Groups["date"].Value, DateFormats, out var electionDate))
            throw new InvalidOperationException(
                $"No General election date parsed from {Config.UpcomingElectionsPageUrl}.");
        if (electionDate.Year != Year)
            throw new InvalidOperationException(
                $"{Config.UpcomingElectionsPageUrl} currently shows a {electionDate.Year} date, not {Year}. " +
                "This page only ever lists the next upcoming election - back-filling a past year isn't supported by this source.");

        var election = NmCandidateMapper.ToElection("General", electionDate, Config.UpcomingElectionsPageUrl);
        RowHelpers.StampState(election, StateCode);
        Console.WriteLine("  Elections found: 1");

        var pageUrl = Config.CandidateListUrl(electionId);
        var rows = HtmlTableParser.Parse(capture.Require(CandidateExportRole).Text());
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

            var candidate = NmCandidateMapper.ToCandidateRow(row, election, pageUrl, FieldMap);
            RowHelpers.StampState(candidate, StateCode);
            result.Candidates.Add(candidate);
        }

        if (result.Candidates.Count == 0)
            throw new InvalidOperationException(
                $"No candidates collected from {pageUrl}; refusing to write hollow outputs. Check manually.");

        Console.WriteLine(
            $"  {election.Name} ({election.ElectionDate:yyyy-MM-dd}): {result.Candidates.Count} candidates, " +
            $"{result.StatewideProposedMeasures.Count} judicial retention questions ({pageUrl})");

        BuildSourcesManifest(result, pageUrl);
        return result;
    }

    private string RequireGeneralElectionId()
    {
        return Config.ElectionId("General") ?? throw new InvalidOperationException(
            $"No election-id/general source parameter in {Config.Links.Origin}. NM's candidate " +
            "portal has no discoverable index of its own election ids - this must be re-derived by hand " +
            "each cycle (see the parameter's notes).");
    }

    private void BuildSourcesManifest(CollectResult result, string candidateListUrl)
    {
        var sources = result.Sources;
        sources.Elections = [new SourceEntry(Config.UpcomingElectionsPageUrl, "html")];
        sources.StatewideCandidates = [new SourceEntry(candidateListUrl, "html")];
        sources.StatewideMeasures = [new SourceEntry(candidateListUrl, "html")];
        sources.VerificationOnly = Config.VerificationSources(Year);
    }
}
