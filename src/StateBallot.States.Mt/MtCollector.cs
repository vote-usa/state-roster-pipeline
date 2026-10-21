using StateBallot.Core;
using StateBallot.Core.Raw;

namespace StateBallot.States.Mt;

/// <summary>
/// Montana state collector, backed by the MT SOS candidate filing portal
/// (a third state, after NM and SD, on the same underlying Telerik RadGrid
/// election-management platform). Unlike NM/SD, no hand-maintained election
/// id is needed at all: the bare candidate-list URL (no election id)
/// redirects to whatever election is presently current, and that page's own
/// "ddlElection" dropdown lists every election - including both the primary
/// and the general - with its own id, name, and real date embedded directly
/// in the option text, so one fetch discovers everything. v1 scope:
/// state/federal/judicial races only - this portal has no county-level
/// offices at all (filed with county clerks separately, not the SOS).
/// </summary>
[StateCode("MT")]
public sealed class MtCollector(int year, string stateDataDir, string? inputDataRoot = null, MtSourceConfig? config = null, SourceLinkSet? links = null)
    : StateCollectorBase<MtSourceConfig>(year, stateDataDir, inputDataRoot, config, links)
{
    protected override IPublishSchedule Schedule { get; } = new MtPublishSchedule();

    public const string ElectionIndexRole = "election-index";
    public const string CandidateListPageRole = "candidate-list-page";
    public const string CandidateExportRole = "candidate-export";

    private static readonly string[] TargetTypes = ["Primary", "General"];

    public override async Task CaptureAsync(HttpFetcher fetcher, DateOnly asOf)
    {
        Console.WriteLine($"Capturing Montana sources for {Year}...");

        var options = ParseElectionOptions(
            await fetcher.GetStringAsync(Config.DefaultCandidateListUrl, FetchTag.Of(ElectionIndexRole)));
        foreach (var type in TargetTypes)
        {
            var found = FindElection(options, type);
            if (found == default)
                continue;

            var pageUrl = Config.CandidateListUrl(found.ElectionId);
            var pageHtml = await fetcher.GetStringAsync(pageUrl, FetchTag.Of(CandidateListPageRole, ("election", found.ElectionId)));
            await WebFormsPostback.TriggerPostbackAsync(
                fetcher, pageUrl, pageHtml, MtSelectors.ExportToCsvEventTarget,
                tag: FetchTag.Of(CandidateExportRole, ("election", found.ElectionId)));
        }
    }

    protected override CollectResult NormalizeCore(CaptureReader capture)
    {
        Console.WriteLine($"Normalizing Montana capture {capture.CaptureId} for {Year}...");

        var options = ParseElectionOptions(capture.Require(ElectionIndexRole).Text());
        ScrapeGuard.RequireAny(options, () =>
            $"No election options parsed from {Config.DefaultCandidateListUrl}'s ddlElection dropdown.");

        var result = new CollectResult();
        var candidateSourceUrls = new List<SourceEntry>();

        foreach (var type in TargetTypes)
        {
            var found = FindElection(options, type);
            if (found == default)
            {
                result.Gaps.Add(
                    $"No {Year} {type} election found in {Config.DefaultCandidateListUrl}'s ddlElection dropdown. " +
                    "Re-run later or check the source manually.");
                continue;
            }

            var pageUrl = Config.CandidateListUrl(found.ElectionId);
            var election = MtCandidateMapper.ToElection(type, found.Date, pageUrl);
            RowHelpers.StampState(election, StateCode);
            result.Elections.Add(election);

            var csvText = capture.Require(CandidateExportRole, ("election", found.ElectionId)).Text();
            csvText = csvText.TrimStart('﻿'); // the export's own UTF-8 BOM, if the payload decoding left it in

            var rows = DelimitedTableParser.Parse(csvText)
                .Where(r => !string.IsNullOrWhiteSpace(r.GetValueOrDefault("Race")))
                .ToList();

            if (rows.Count == 0)
            {
                result.Gaps.Add($"{election.Name} ({found.Date:yyyy-MM-dd}): no rows parsed from {pageUrl}. Re-run later.");
                result.PendingElections.Add(election);
                continue;
            }

            candidateSourceUrls.Add(new SourceEntry(pageUrl, "csv"));
            foreach (var row in rows)
            {
                var candidate = MtCandidateMapper.ToCandidateRow(row, election, pageUrl, FieldMap);
                RowHelpers.StampState(candidate, StateCode);
                result.Candidates.Add(candidate);
            }

            Console.WriteLine($"  {election.Name} ({found.Date:yyyy-MM-dd}): {rows.Count} candidates ({pageUrl})");
        }

        BuildSourcesManifest(result, candidateSourceUrls);
        return result;
    }

    private (string ElectionId, string Type, DateOnly Date) FindElection(
        List<(string ElectionId, string Type, DateOnly Date)> options, string type) =>
        options.FirstOrDefault(o => string.Equals(o.Type, type, StringComparison.OrdinalIgnoreCase) && o.Date.Year == Year);

    /// <summary>Extracts every (electionId, type, date) triple from the page's own "ddlElection" dropdown.</summary>
    private static List<(string ElectionId, string Type, DateOnly Date)> ParseElectionOptions(string html)
    {
        var results = new List<(string, string, DateOnly)>();
        var block = MtSelectors.ElectionDropdownBlock.Match(html);
        if (!block.Success)
            return results;

        foreach (System.Text.RegularExpressions.Match option in MtSelectors.OptionTag.Matches(block.Groups["options"].Value))
        {
            var value = option.Groups["value"].Value;
            if (value.Length == 0)
                continue;
            var textMatch = MtSelectors.ElectionOptionText.Match(option.Groups["text"].Value.Trim());
            if (!textMatch.Success)
                continue;
            if (!DateOnly.TryParse(textMatch.Groups["date"].Value, out var date))
                continue;
            results.Add((value, textMatch.Groups["type"].Value, date));
        }

        return results;
    }

    private void BuildSourcesManifest(CollectResult result, List<SourceEntry> candidateListUrls)
    {
        var sources = result.Sources;
        sources.Elections = [new SourceEntry(Config.DefaultCandidateListUrl, "html")];
        sources.StatewideCandidates = candidateListUrls;
        sources.VerificationOnly = Config.VerificationSources(Year);
    }
}
