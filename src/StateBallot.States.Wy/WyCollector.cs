using StateBallot.Core;
using StateBallot.Core.Raw;

namespace StateBallot.States.Wy;

/// <summary>
/// Wyoming state collector, backed by the WY SOS elections site. Both the
/// primary and general elections' candidate lists are separate static CSVs
/// (no bot-blocking, no session/auth), and neither carries the election's
/// actual date - that's scraped separately from the info page's JSON-LD (see
/// ElectionDateScraper). v1 scope: candidates only - WY's export has no
/// county concept at all, and its ballot-measure source is full-text-only PDF
/// (not attempted here).
/// </summary>
[StateCode("WY")]
public sealed class WyCollector(int year, string stateDataDir, string? inputDataRoot = null, WySourceConfig? config = null)
    : StateCollectorBase<WySourceConfig>(year, stateDataDir, inputDataRoot, config)
{
    protected override string SourceHomeUrl => Config.BaseUrl;
    protected override IPublishSchedule Schedule { get; } = new WyPublishSchedule();

    public const string CandidateListRole = "candidate-list";

    public override async Task CaptureAsync(HttpFetcher fetcher, DateOnly asOf)
    {
        Console.WriteLine($"Capturing Wyoming sources for {Year}...");

        var elections = await new ElectionDateScraper(Config, DateFormats).CaptureAsync(fetcher, Year);
        foreach (var type in elections.Select(e => e.ElectionType).Distinct())
            await fetcher.GetStringAsync(Config.CandidateListUrl(Year, type), FetchTag.Of(CandidateListRole, ("type", type)));
    }

    protected override CollectResult NormalizeCore(CaptureReader capture)
    {
        Console.WriteLine($"Normalizing Wyoming capture {capture.CaptureId} for {Year}...");

        var elections = new ElectionDateScraper(Config, DateFormats).Parse(capture.Require(ElectionDateScraper.Role).Text(), Year);
        Console.WriteLine($"  Elections found: {elections.Count}");
        RowHelpers.StampState(elections, StateCode);

        var result = new CollectResult();
        result.Elections.AddRange(elections);

        foreach (var election in elections)
        {
            var url = Config.CandidateListUrl(Year, election.ElectionType);
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
                var candidate = WyCandidateMapper.ToCandidateRow(row, election, url, FieldMap);
                RowHelpers.StampState(candidate, StateCode);
                result.Candidates.Add(candidate);
            }

            Console.WriteLine($"  {election.Name} ({election.ElectionDate:yyyy-MM-dd}): {rows.Count} candidates ({url})");
        }

        BuildSourcesManifest(result, elections);
        return result;
    }

    private void BuildSourcesManifest(CollectResult result, List<Election> elections)
    {
        var sources = result.Sources;
        sources.Elections = [new SourceEntry(Config.ElectionInfoPageUrl(Year), "html")];
        sources.StatewideCandidates = elections
            .Select(e => new SourceEntry(Config.CandidateListUrl(Year, e.ElectionType), "csv"))
            .ToList();
        sources.VerificationOnly = [new SourceEntry($"https://ballotpedia.org/Wyoming_elections,_{Year}", "html")];
    }
}
