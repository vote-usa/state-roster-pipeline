using StateBallot.Core;
using StateBallot.Core.Raw;

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
public sealed class NeCollector(int year, string stateDataDir, string? inputDataRoot = null, NeSourceConfig? config = null, SourceLinkSet? links = null)
    : StateCollectorBase<NeSourceConfig>(year, stateDataDir, inputDataRoot, config, links)
{
    protected override IPublishSchedule Schedule { get; } = new NePublishSchedule();

    public const string CandidateFilingListRole = "candidate-filing-list";

    public override async Task CaptureAsync(HttpFetcher fetcher, DateOnly asOf)
    {
        Console.WriteLine($"Capturing Nebraska sources for {Year}...");
        await ElectionDateScraper.CaptureAsync(fetcher, Config);
        await fetcher.GetBytesAsync(Config.StatewideCandidateFilingListUrl(Year), FetchTag.Of(CandidateFilingListRole));
    }

    protected override CollectResult NormalizeCore(CaptureReader capture)
    {
        Console.WriteLine($"Normalizing Nebraska capture {capture.CaptureId} for {Year}...");

        var (primary, general) = new ElectionDateScraper(Config, DateFormats).Parse(capture.Require(ElectionDateScraper.Role).Text(), Year);
        Console.WriteLine($"  {primary.Name} ({primary.ElectionDate:yyyy-MM-dd}), {general.Name} ({general.ElectionDate:yyyy-MM-dd})");
        RowHelpers.StampState([primary, general], StateCode);

        var url = Config.StatewideCandidateFilingListUrl(Year);
        var xlsxBytes = capture.Require(CandidateFilingListRole).Bytes();

        // The workbook is a live "currently filed" snapshot, not a fixed
        // as-of-filing-deadline list (same idea as MS's CSV export): before the
        // primary it lists every filed candidate per partisan office (a
        // contested field, multiple per party); after the primary it's already
        // narrowed to the resolved nominees. There's no per-row date to key off
        // like MS has, so the whole sheet is attributed to whichever election
        // was next on the day of the capture vs. the primary's own date - a known
        // simplification for local nonpartisan races that never have a primary
        // at all (they're General-bound regardless of this comparison, so nothing
        // is actually miscategorized for those).
        var today = capture.AsOf;
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
            var candidate = NeCandidateMapper.ToCandidateRow(row, candidateElection, url, FieldMap);
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

        BuildSourcesManifest(result, url);
        return result;
    }

    private void BuildSourcesManifest(CollectResult result, string candidateListUrl)
    {
        var sources = result.Sources;
        sources.Elections = [new SourceEntry(Config.ElectionsPageUrl, "html")];
        sources.StatewideCandidates = [new SourceEntry(candidateListUrl, "xlsx")];
        sources.StatewideMeasures = [new SourceEntry(candidateListUrl, "xlsx")];
        sources.VerificationOnly = Config.VerificationSources(Year);
    }
}
