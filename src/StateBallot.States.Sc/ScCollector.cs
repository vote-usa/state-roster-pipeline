using System.Text.Json;
using StateBallot.Core;
using StateBallot.Core.Raw;

namespace StateBallot.States.Sc;

/// <summary>
/// South Carolina state collector, backed by the SC Votes (VREMS) candidate
/// portal. Both the primary and general elections' ids, names, and real
/// dates come from one JSON API call (GetElections) - no separate
/// election-date source needed, unlike every other state onboarded so far.
/// The candidate search itself is a stateless form POST (no session/cookie/
/// antiforgery-token setup needed, confirmed live) that returns every
/// candidate for that election in one un-paginated HTML table - both parties
/// together despite looking, from the outside research spreadsheet alone,
/// like a per-party split source (see ScSourceConfig/ScCollector docs and
/// the write-up in configDrivenPipelineInfo.md - same lesson NM's own
/// "looked split, wasn't" correction already taught). v1 scope: the
/// statewide primary and general only - same-cycle special elections and
/// off-cycle local elections (a separate "Local" election kind) are not
/// attempted, nor is the parallel referendum search the same portal offers.
/// </summary>
[StateCode("SC")]
public sealed class ScCollector(int year, string stateDataDir, string? inputDataRoot = null, ScSourceConfig? config = null, SourceLinkSet? links = null)
    : StateCollectorBase<ScSourceConfig>(year, stateDataDir, inputDataRoot, config, links)
{
    protected override IPublishSchedule Schedule { get; } = new ScPublishSchedule();

    public const string ElectionsRole = "elections";
    public const string CandidateSearchRole = "candidate-search";

    private static readonly (string Type, string Name)[] TargetElections =
        [("Primary", ScSelectors.PrimaryElectionName), ("General", ScSelectors.GeneralElectionName)];

    public override async Task CaptureAsync(HttpFetcher fetcher, DateOnly asOf)
    {
        Console.WriteLine($"Capturing South Carolina sources for {Year}...");

        var available = ParseElections(await fetcher.GetStringAsync(Config.ElectionsByYearUrl(Year), FetchTag.Of(ElectionsRole)));
        foreach (var (_, name) in TargetElections)
        {
            var found = FindElection(available, name);
            if (found is not null)
                await fetcher.PostFormAsync(
                    Config.CandidateSearchUrl,
                    new Dictionary<string, string> { ["ElectionId"] = found.ElectionId },
                    FetchTag.Of(CandidateSearchRole, ("election", found.ElectionId)));
        }
    }

    protected override CollectResult NormalizeCore(CaptureReader capture)
    {
        Console.WriteLine($"Normalizing South Carolina capture {capture.CaptureId} for {Year}...");

        var electionsUrl = Config.ElectionsByYearUrl(Year);
        var available = ParseElections(capture.Require(ElectionsRole).Text());
        ScrapeGuard.RequireAny(available, () => $"No elections at all returned from {electionsUrl} for {Year}.");

        var result = new CollectResult();
        var candidateSourceUrls = new List<SourceEntry>();

        foreach (var (type, name) in TargetElections)
        {
            var found = FindElection(available, name);
            if (found is null)
            {
                result.Gaps.Add($"'{name}' not found among {Year}'s elections at {electionsUrl}. Re-run later or check the source manually.");
                continue;
            }

            var electionDate = DateOnly.FromDateTime(found.ElectionDate);
            var election = ScCandidateMapper.ToElection(type, electionDate, found.ElectionId, electionsUrl);
            RowHelpers.StampState(election, StateCode);
            result.Elections.Add(election);

            var html = capture.Require(CandidateSearchRole, ("election", found.ElectionId)).Text();
            var rows = HtmlTableParser.Parse(html);
            var sourceUrl = $"{Config.CandidateSearchUrl}?ElectionId={found.ElectionId}";

            if (rows.Count == 0)
            {
                result.Gaps.Add(
                    $"{election.Name} ({electionDate:yyyy-MM-dd}): no rows parsed from {sourceUrl}. Re-run later.");
                result.PendingElections.Add(election);
                continue;
            }

            candidateSourceUrls.Add(new SourceEntry(sourceUrl, "html"));
            foreach (var row in rows)
            {
                var candidate = ScCandidateMapper.ToCandidateRow(row, election, sourceUrl, FieldMap);
                RowHelpers.StampState(candidate, StateCode);
                result.Candidates.Add(candidate);
            }

            Console.WriteLine($"  {election.Name} ({electionDate:yyyy-MM-dd}): {rows.Count} candidates ({sourceUrl})");
        }

        BuildSourcesManifest(result, electionsUrl, candidateSourceUrls);
        return result;
    }

    private static List<ScElection> ParseElections(string json) =>
        JsonSerializer.Deserialize<List<ScElection>>(json) ?? [];

    private static ScElection? FindElection(List<ScElection> available, string name) =>
        available.FirstOrDefault(e => string.Equals(e.ElectionName, name, StringComparison.OrdinalIgnoreCase));

    private void BuildSourcesManifest(CollectResult result, string electionsUrl, List<SourceEntry> candidateListUrls)
    {
        var sources = result.Sources;
        sources.Elections = [new SourceEntry(electionsUrl, "json")];
        sources.StatewideCandidates = candidateListUrls;
        sources.VerificationOnly = Config.VerificationSources(Year);
    }
}
