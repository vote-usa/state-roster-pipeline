using StateBallot.Core;

namespace StateBallot.States.Ms;

/// <summary>
/// Mississippi state collector, backed by the SOS's single "Candidate
/// Qualifying List" export - an ASP.NET WebForms page whose "Download CSV"
/// button (see WebFormsPostback, same mechanism as HI) returns the state's
/// full current candidate roster in one response: U.S. Senate/House,
/// nonpartisan judicial races (Court of Appeals, Chancery Court, Circuit
/// Court), and any active special elections, all in one file with no
/// separate per-election fetch. See MsSourceConfig for why this source needs
/// a curl-like User-Agent rather than TX's browser-spoofing headers. v1
/// scope: candidates only - no county concept in this export (judicial races
/// carry their own district/place numbering instead) and no ballot-measure
/// source attempted.
/// </summary>
[StateCode("MS")]
public sealed class MsCollector : IStateCollector
{
    private readonly HttpFetcher _fetcher;
    private readonly MsSourceConfig _config;
    private readonly IPublishSchedule _schedule;
    private readonly int _year;
    private readonly string _stateDataDir;
    private readonly string _inputDataRoot;

    public string StateCode => "MS";

    /// <param name="stateDataDir">Per-state output directory (data/output/&lt;xx&gt;/). Inputs are under data/input/&lt;xx&gt;/,
    /// including date_formats.json.</param>
    public MsCollector(
        HttpFetcher fetcher, int year, string stateDataDir, string? inputDataRoot = null, MsSourceConfig? config = null)
    {
        _fetcher = fetcher;
        _year = year;
        _stateDataDir = stateDataDir;
        _inputDataRoot = ResolveInputDataRoot(stateDataDir, inputDataRoot);
        _config = config ?? new MsSourceConfig();
        _schedule = new MsPublishSchedule();

        // The Akamai WAF in front of sos.ms.gov blocks realistic browser UAs
        // (and HttpFetcher's own default) but lets plain HTTP-tool UAs like
        // curl through untouched - see MsSourceConfig. Assumes one HttpFetcher
        // per single-state run (same caveat as TxCollector's header stamping).
        foreach (var (name, value) in MsSourceConfig.ExtraHeaders)
            _fetcher.AddDefaultHeader(name, value);
    }

    public async Task<CollectResult> CollectAsync()
    {
        Console.WriteLine($"Collecting Mississippi ballot roster for {_year}...");

        var dataRoot = _inputDataRoot;
        var dateFormats = DateFormatConfig.Load(DataPaths.DateFormatsPath(dataRoot, StateCode));
        var fieldMap = LookupTableLoader.Load(DataPaths.CandidateFieldMapPath(dataRoot, StateCode));

        var url = _config.CandidateQualifyingListUrl;
        var html = await _fetcher.GetStringAsync(url);
        var csvText = await WebFormsPostback.ClickButtonAsync(_fetcher, url, html, MsSourceConfig.DownloadCsvButtonName);

        var rows = DelimitedTableParser.Parse(csvText)
            .Where(r => r.GetValueOrDefault("Candidate Name", "").Trim().Length > 0) // drop the trailing empty placeholder row
            .ToList();
        ScrapeGuard.RequireAny(rows, () => $"No candidate rows parsed from the CSV export at {url}.");

        var elections = DiscoverElections(rows, dateFormats, url);
        Console.WriteLine($"  Elections found in the file: {elections.Count}");
        RowHelpers.StampState(elections, StateCode);

        var result = new CollectResult();
        result.Elections.AddRange(elections);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        foreach (var row in rows)
        {
            var election = MsCandidateMapper.DetermineElection(row, elections, today, dateFormats);
            var candidate = MsCandidateMapper.ToCandidateRow(row, election, url, fieldMap);
            RowHelpers.StampState(candidate, StateCode);
            result.Candidates.Add(candidate);
        }

        foreach (var election in elections)
        {
            var count = result.Candidates.Count(c => c.ElectionDate == election.ElectionDate.ToString("yyyy-MM-dd"));
            Console.WriteLine($"  {election.Name} ({election.ElectionDate:yyyy-MM-dd}): {count} candidates");
        }

        if (result.Candidates.Count == 0)
            throw new InvalidOperationException(
                "No candidates collected; refusing to write hollow outputs. Check https://sos.ms.gov manually.");

        CollectResultSorter.Sort(result);
        BuildSourcesManifest(result, url);
        return result;
    }

    /// <summary>
    /// Every row carries a Primary Election and General Election date (federal
    /// partisan races) or a single Election date (nonpartisan judicial races and
    /// specials) - see MsCandidateMapper.DetermineElection for how a row is
    /// attributed to one of these. Primary/General are expected to appear
    /// exactly once each across the whole file; any distinct Election-column
    /// date beyond those two is a genuinely separate special election.
    /// </summary>
    private static List<Election> DiscoverElections(List<Dictionary<string, string>> rows, string[] dateFormats, string sourceUrl)
    {
        var primaryDate = ParseFirst(rows, "Primary Election", dateFormats)
            ?? throw new InvalidOperationException($"No parseable 'Primary Election' date found in {sourceUrl}.");
        var generalDate = ParseFirst(rows, "General Election", dateFormats)
            ?? throw new InvalidOperationException($"No parseable 'General Election' date found in {sourceUrl}.");

        var elections = new List<Election>
        {
            MsCandidateMapper.ToElection("Primary", primaryDate, sourceUrl),
            MsCandidateMapper.ToElection("General", generalDate, sourceUrl),
        };

        var specialDates = rows
            .Select(r => r.GetValueOrDefault("Election", ""))
            .Where(d => d.Length > 0)
            .Select(d => DateParsing.TryParseAny(d, dateFormats, out var date) ? date : (DateOnly?)null)
            .Where(d => d is not null && d != primaryDate && d != generalDate)
            .Select(d => d!.Value)
            .Distinct()
            .OrderBy(d => d);

        elections.AddRange(specialDates.Select(d => MsCandidateMapper.ToElection("Special", d, sourceUrl)));
        return elections;
    }

    private static DateOnly? ParseFirst(List<Dictionary<string, string>> rows, string column, string[] dateFormats) =>
        rows
            .Select(r => r.GetValueOrDefault(column, ""))
            .Where(d => d.Length > 0)
            .Select(d => DateParsing.TryParseAny(d, dateFormats, out var date) ? date : (DateOnly?)null)
            .FirstOrDefault(d => d is not null);

    private void BuildSourcesManifest(CollectResult result, string url)
    {
        var sources = result.Sources;
        sources.Elections = [new SourceEntry(url, "csv (via WebForms POST)")];
        sources.StatewideCandidates = [new SourceEntry(url, "csv (via WebForms POST)")];
        sources.VerificationOnly = [new SourceEntry($"https://ballotpedia.org/Mississippi_elections,_{_year}", "html")];
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
