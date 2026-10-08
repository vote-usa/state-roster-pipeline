using StateBallot.Core;
using StateBallot.Core.Raw;

namespace StateBallot.States.Md;

/// <summary>
/// Maryland state collector, backed by the MD State Board of Elections site.
/// v1 scope: statewide candidates only (Governor through judicial races) for
/// whichever election(s) the SBE's landing page currently lists as upcoming.
/// Deliberately not yet covered: local/county races (MD publishes those in a
/// separate "all_counties" CSV export whose "Contest Run By..." column mixes
/// county names and within-county district labels - attributing each row to
/// its county needs order-dependent parsing not attempted here), ballot
/// measures (no MD ballot-measure source was found), and a county directory.
/// </summary>
[StateCode("MD")]
public sealed class MdCollector(int year, string stateDataDir, string? inputDataRoot = null, MdSourceConfig? config = null, SourceLinkSet? links = null)
    : StateCollectorBase<MdSourceConfig>(year, stateDataDir, inputDataRoot, config, links)
{
    protected override IPublishSchedule Schedule { get; } = new MdPublishSchedule();

    public const string CandidateListRole = "candidate-list";

    public override async Task CaptureAsync(HttpFetcher fetcher, DateOnly asOf)
    {
        Console.WriteLine($"Capturing Maryland sources for {Year}...");

        var elections = await new ElectionDayScraper(Config, DateFormats).CaptureAsync(fetcher, Year);
        foreach (var type in elections.Select(e => e.ElectionType).Distinct())
            await fetcher.GetStringAsync(Config.StatewideCandidateListUrl(Year, type), FetchTag.Of(CandidateListRole, ("type", type)));
    }

    protected override CollectResult NormalizeCore(CaptureReader capture)
    {
        Console.WriteLine($"Normalizing Maryland capture {capture.CaptureId} for {Year}...");

        var elections = new ElectionDayScraper(Config, DateFormats).Parse(capture.Require(ElectionDayScraper.Role).Text(), Year);
        Console.WriteLine($"  Election day entries found: {elections.Count}");
        RowHelpers.StampState(elections, StateCode);

        var result = new CollectResult();
        result.Elections.AddRange(elections);

        foreach (var election in elections)
        {
            Console.WriteLine($"  {election.Name} ({election.ElectionDate:yyyy-MM-dd})...");
            var url = Config.StatewideCandidateListUrl(Year, election.ElectionType);
            var csvText = capture.Require(CandidateListRole, ("type", election.ElectionType)).Text();
            var rows = DelimitedTableParser.Parse(csvText);

            if (rows.Count == 0)
            {
                result.Gaps.Add(
                    $"{election.Name} ({election.ElectionDate:yyyy-MM-dd}): no rows parsed from {url}; " +
                    "the candidate list may not be published yet. Re-run later.");
                result.PendingElections.Add(election);
                continue;
            }

            foreach (var row in rows)
            {
                var candidate = MdCandidateMapper.ToCandidateRow(row, election, url, FieldMap);
                RowHelpers.StampState(candidate, StateCode);
                result.Candidates.Add(candidate);
            }

            Console.WriteLine($"    {rows.Count} candidates from statewide list ({url})");
        }

        BuildSourcesManifest(result, elections);
        return result;
    }

    private void BuildSourcesManifest(CollectResult result, List<Election> elections)
    {
        var sources = result.Sources;
        sources.Elections = [new SourceEntry(Config.ElectionsPageUrl(Year), "html")];
        sources.StatewideCandidates = elections
            .Select(e => new SourceEntry(Config.StatewideCandidateListUrl(Year, e.ElectionType), "csv"))
            .ToList();
        sources.VerificationOnly = Config.VerificationSources(Year);
    }
}
