using StateBallot.Core;
using StateBallot.Core.Raw;

namespace StateBallot.States.Vt;

/// <summary>
/// Vermont state collector, backed by the VT SOS elections site. The
/// Primary/General candidate lists are separate static XLSX files at
/// year-templated, stable URLs (no bot-blocking, no session/auth) - unlike
/// CO's equivalent pages, both back-fill years and the current cycle work the
/// same way. Neither XLSX carries the election's actual date - that's scraped
/// separately from the (evergreen, current-cycle-only) candidates page's own
/// plain text (see VtElectionDateScraper), so only the current cycle's dates
/// are ever available even though past years' candidate rosters are.
/// </summary>
[StateCode("VT")]
public sealed class VtCollector(int year, string stateDataDir, string? inputDataRoot = null, VtSourceConfig? config = null, SourceLinkSet? links = null)
    : StateCollectorBase<VtSourceConfig>(year, stateDataDir, inputDataRoot, config, links)
{
    protected override IPublishSchedule Schedule { get; } = new VtPublishSchedule();

    public const string CandidateListRole = "candidate-list";

    public override async Task CaptureAsync(HttpFetcher fetcher, DateOnly asOf)
    {
        Console.WriteLine($"Capturing Vermont sources for {Year}...");

        var found = await new VtElectionDateScraper(Config, DateFormats).TryCaptureAsync(fetcher, Year);
        if (found is null)
            return;

        foreach (var election in new[] { found.Value.Primary, found.Value.General })
            await fetcher.TryGetBytesAsync(
                Config.CandidateListUrl(Year, election.ElectionType), FetchTag.Of(CandidateListRole, ("type", election.ElectionType)));
    }

    protected override CollectResult NormalizeCore(CaptureReader capture)
    {
        Console.WriteLine($"Normalizing Vermont capture {capture.CaptureId} for {Year}...");

        var found = new VtElectionDateScraper(Config, DateFormats).TryParse(capture.Require(VtElectionDateScraper.Role).Text(), Year)
            ?? throw new InvalidOperationException(
                $"{Config.CandidatesPageUrl} doesn't currently show {Year}'s election dates (it's an evergreen " +
                "page that only ever reflects the current cycle). Back-filling a past year's dates isn't supported by this source.");
        List<Election> elections = [found.Primary, found.General];
        Console.WriteLine($"  Elections found: {elections.Count}");
        RowHelpers.StampState(elections, StateCode);

        var result = new CollectResult();
        result.Elections.AddRange(elections);
        var candidateListUrls = new List<SourceEntry>();

        foreach (var election in elections)
        {
            var xlsxUrl = Config.CandidateListUrl(Year, election.ElectionType);
            candidateListUrls.Add(new SourceEntry(xlsxUrl, "xlsx"));
            var captured = capture.Require(CandidateListRole, ("type", election.ElectionType));

            if (!captured.HasPayload)
            {
                result.Gaps.Add(
                    $"{election.Name} ({election.ElectionDate:yyyy-MM-dd}): {xlsxUrl} not published yet. Re-run later.");
                result.PendingElections.Add(election);
                continue;
            }

            // Every genuine row has a Contest; guards against any stray blank
            // formatting row ClosedXML's used-range picks up ahead of the real header.
            var rows = XlsxTableParser.Parse(captured.Bytes())
                .Where(r => !string.IsNullOrWhiteSpace(r.GetValueOrDefault("Contest")))
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
                var candidate = VtCandidateMapper.ToCandidateRow(row, election, xlsxUrl, FieldMap);
                RowHelpers.StampState(candidate, StateCode);
                result.Candidates.Add(candidate);
            }

            Console.WriteLine($"  {election.Name} ({election.ElectionDate:yyyy-MM-dd}): {rows.Count} candidates ({xlsxUrl})");
        }

        BuildSourcesManifest(result, candidateListUrls);
        return result;
    }

    private void BuildSourcesManifest(CollectResult result, List<SourceEntry> candidateListUrls)
    {
        var sources = result.Sources;
        sources.Elections = [new SourceEntry(Config.CandidatesPageUrl, "html")];
        sources.StatewideCandidates = candidateListUrls;
        sources.VerificationOnly = Config.VerificationSources(Year);
    }
}
