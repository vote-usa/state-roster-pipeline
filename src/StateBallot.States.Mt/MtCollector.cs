using StateBallot.Core;

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
public sealed class MtCollector : IStateCollector
{
    private readonly HttpFetcher _fetcher;
    private readonly MtSourceConfig _config;
    private readonly IPublishSchedule _schedule;
    private readonly int _year;
    private readonly string _stateDataDir;
    private readonly string _inputDataRoot;

    public string StateCode => "MT";

    /// <param name="stateDataDir">Per-state output directory (data/output/&lt;xx&gt;/). Inputs are under data/input/&lt;xx&gt;/.</param>
    public MtCollector(
        HttpFetcher fetcher, int year, string stateDataDir, string? inputDataRoot = null, MtSourceConfig? config = null)
    {
        _fetcher = fetcher;
        _year = year;
        _stateDataDir = stateDataDir;
        _inputDataRoot = ResolveInputDataRoot(stateDataDir, inputDataRoot);
        _config = config ?? new MtSourceConfig();
        _schedule = new MtPublishSchedule();
    }

    public async Task<CollectResult> CollectAsync()
    {
        Console.WriteLine($"Collecting Montana ballot roster for {_year}...");

        var dataRoot = _inputDataRoot;
        var fieldMap = LookupTableLoader.Load(DataPaths.CandidateFieldMapPath(dataRoot, StateCode));

        var indexHtml = await _fetcher.GetStringAsync(_config.DefaultCandidateListUrl);
        var options = ParseElectionOptions(indexHtml);
        ScrapeGuard.RequireAny(options, () =>
            $"No election options parsed from {_config.DefaultCandidateListUrl}'s ddlElection dropdown.");

        var result = new CollectResult();
        var candidateSourceUrls = new List<SourceEntry>();

        foreach (var type in new[] { "Primary", "General" })
        {
            var found = options.FirstOrDefault(o =>
                string.Equals(o.Type, type, StringComparison.OrdinalIgnoreCase) && o.Date.Year == _year);
            if (found == default)
            {
                result.Gaps.Add(
                    $"No {_year} {type} election found in {_config.DefaultCandidateListUrl}'s ddlElection dropdown. " +
                    "Re-run later or check the source manually.");
                continue;
            }

            var pageUrl = _config.CandidateListUrl(found.ElectionId);
            var election = MtCandidateMapper.ToElection(type, found.Date, pageUrl);
            RowHelpers.StampState(election, StateCode);
            result.Elections.Add(election);

            var pageHtml = await _fetcher.GetStringAsync(pageUrl);
            var csvText = await WebFormsPostback.TriggerPostbackAsync(_fetcher, pageUrl, pageHtml, MtSelectors.ExportToCsvEventTarget);
            csvText = csvText.TrimStart('﻿'); // the export's own UTF-8 BOM, if HttpFetcher's decoding left it in

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
                var candidate = MtCandidateMapper.ToCandidateRow(row, election, pageUrl, fieldMap);
                RowHelpers.StampState(candidate, StateCode);
                result.Candidates.Add(candidate);
            }

            Console.WriteLine($"  {election.Name} ({found.Date:yyyy-MM-dd}): {rows.Count} candidates ({pageUrl})");
        }

        if (result.Candidates.Count == 0)
            throw new InvalidOperationException(
                "No candidates collected for any discovered election; refusing to write hollow outputs. " +
                $"Check {_config.DefaultCandidateListUrl} manually.");

        CollectResultSorter.Sort(result);
        BuildSourcesManifest(result, candidateSourceUrls);
        return result;
    }

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
        sources.Elections = [new SourceEntry(_config.DefaultCandidateListUrl, "html")];
        sources.StatewideCandidates = candidateListUrls;
        sources.VerificationOnly = [new SourceEntry($"https://ballotpedia.org/Montana_elections,_{_year}", "html")];
        sources.NextRun = _schedule.Recommend(result, _year);
    }

    private static string ResolveInputDataRoot(string stateDataDir, string? inputDataRoot)
    {
        if (!string.IsNullOrWhiteSpace(inputDataRoot))
            return Path.GetFullPath(inputDataRoot);
        return DataPaths.TryInferPipelineDataRoot(stateDataDir)
            ?? throw new InvalidOperationException(
                $"Cannot infer input data root from output dir '{stateDataDir}'. " +
                "Pass inputDataRoot (CLI --input-root) when writing outside data/output/<xx>.");
    }
}
