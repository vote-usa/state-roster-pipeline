using StateBallot.Core;
using StateBallot.Core.Raw;

namespace StateBallot.States.Hi;

/// <summary>
/// Hawaii state collector, backed by the HI Office of Elections sites. One
/// candidate export covers the entire election cycle (both Primary and
/// General); each row's own Status says which one it's actually in (see
/// HiCandidateMapper.DetermineElection), since the export itself carries no
/// per-row election date/id. v1 scope: candidates only - HI's export has no
/// county concept (county-level races are just contests like every other
/// office), and no ballot-measure source was found for HI.
/// </summary>
[StateCode("HI")]
public sealed class HiCollector(int year, string stateDataDir, string? inputDataRoot = null, HiSourceConfig? config = null)
    : StateCollectorBase<HiSourceConfig>(year, stateDataDir, inputDataRoot, config)
{
    protected override string SourceHomeUrl => Config.OlvrBaseUrl;
    protected override IPublishSchedule Schedule { get; } = new HiPublishSchedule();

    public override async Task CaptureAsync(HttpFetcher fetcher, DateOnly asOf)
    {
        Console.WriteLine($"Capturing Hawaii sources for {Year}...");
        await ElectionDateScraper.CaptureAsync(fetcher, Config);
        await new CandidateExportClient(Config).CaptureAsync(fetcher, Year);
    }

    protected override CollectResult NormalizeCore(CaptureReader capture)
    {
        Console.WriteLine($"Normalizing Hawaii capture {capture.CaptureId} for {Year}...");

        var (primary, general) = new ElectionDateScraper(Config, DateFormats).Parse(capture.Require(ElectionDateScraper.Role).Text(), Year);
        Console.WriteLine($"  {primary.Name} ({primary.ElectionDate:yyyy-MM-dd}), {general.Name} ({general.ElectionDate:yyyy-MM-dd})");
        RowHelpers.StampState([primary, general], StateCode);

        var (rows, pageUrl) = new CandidateExportClient(Config).Parse(capture, Year);
        Console.WriteLine($"  {rows.Count} candidate rows exported from {pageUrl}");

        var result = new CollectResult();
        result.Elections.Add(primary);
        result.Elections.Add(general);

        foreach (var row in rows)
        {
            var election = HiCandidateMapper.DetermineElection(row.GetValueOrDefault("Status", ""), primary, general);
            var candidate = HiCandidateMapper.ToCandidateRow(row, election, pageUrl, FieldMap);
            RowHelpers.StampState(candidate, StateCode);
            result.Candidates.Add(candidate);
        }

        BuildSourcesManifest(result, pageUrl);
        return result;
    }

    private void BuildSourcesManifest(CollectResult result, string pageUrl)
    {
        var sources = result.Sources;
        sources.Elections = [new SourceEntry(Config.ElectionsHomeUrl, "html")];
        sources.StatewideCandidates = [new SourceEntry(pageUrl, "csv")];
        sources.VerificationOnly = [new SourceEntry($"https://ballotpedia.org/Hawaii_elections,_{Year}", "html")];
    }
}
