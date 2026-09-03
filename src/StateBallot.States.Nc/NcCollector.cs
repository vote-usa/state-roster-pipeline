using StateBallot.Core;

namespace StateBallot.States.Nc;

/// <summary>
/// North Carolina state collector, backed by the NC SBE's single candidate
/// filing export. Unlike every other state so far, this one CSV already is a
/// full per-county ballot listing: every row carries its own county_name and
/// election_dt, and statewide/multi-county candidates simply repeat once per
/// county (100 counties) rather than needing a separate discovery step.
/// v1 scope: candidates only (both the primary and general elections the file
/// covers) with full county-ballot attribution. No ballot measures (NC's are
/// published as PDF, not attempted here) and no county directory.
/// </summary>
[StateCode("NC")]
public sealed class NcCollector : IStateCollector
{
    private readonly HttpFetcher _fetcher;
    private readonly NcSourceConfig _config;
    private readonly IPublishSchedule _schedule;
    private readonly int _year;
    private readonly string _stateDataDir;

    public string StateCode => "NC";

    /// <param name="stateDataDir">Per-state output directory (data/output/&lt;xx&gt;/). Inputs are under data/input/&lt;xx&gt;/,
    /// including date_formats.json.</param>
    public NcCollector(HttpFetcher fetcher, int year, string stateDataDir, NcSourceConfig? config = null)
    {
        _fetcher = fetcher;
        _year = year;
        _stateDataDir = stateDataDir;
        _config = config ?? new NcSourceConfig();
        _schedule = new NcPublishSchedule();
    }

    public async Task<CollectResult> CollectAsync()
    {
        Console.WriteLine($"Collecting North Carolina ballot roster for {_year}...");

        var (dataRoot, _) = DataPaths.FromStateOutputDir(_stateDataDir);
        var dateFormats = DateFormatConfig.Load(DataPaths.DateFormatsPath(dataRoot, StateCode));

        var url = _config.CandidateListingUrl(_year);
        var csvText = await _fetcher.GetStringAsync(url);
        var rows = DelimitedTableParser.Parse(csvText);
        ScrapeGuard.RequireAny(rows, () => $"No rows parsed from {url}. The file may be empty or malformed.");

        var elections = DiscoverElections(rows, dateFormats, url);
        Console.WriteLine($"  Elections found in the file: {elections.Count}");
        RowHelpers.StampState(elections, StateCode);

        var result = new CollectResult();
        result.Elections.AddRange(elections);

        foreach (var election in elections)
        {
            var electionRows = rows.Where(r => MatchesElectionDate(r, election, dateFormats)).ToList();
            Console.WriteLine($"  {election.Name} ({election.ElectionDate:yyyy-MM-dd}): {electionRows.Count} ballot lines");

            CollectStatewideCandidates(electionRows, election, url, result);
            CollectCountyBallots(electionRows, election, url, result);
        }

        if (result.Candidates.Count == 0)
            throw new InvalidOperationException(
                "No statewide/district candidates collected; refusing to write hollow outputs. " +
                "Check https://www.ncsbe.gov manually.");

        CollectResultSorter.Sort(result);
        BuildSourcesManifest(result, url);
        return result;
    }

    private static List<Election> DiscoverElections(List<Dictionary<string, string>> rows, string[] dateFormats, string sourceUrl)
    {
        var dates = rows
            .Select(r => r.GetValueOrDefault("election_dt", ""))
            .Where(d => d.Length > 0)
            .Distinct()
            .Select(d => DateParsing.TryParseAny(d, dateFormats, out var date) ? date : (DateOnly?)null)
            .Where(d => d is not null)
            .Select(d => d!.Value)
            .Distinct()
            .ToList();
        ScrapeGuard.RequireAny(dates, () => $"No parseable election_dt values found in {sourceUrl}.");

        var latest = dates.Max();
        return dates.Select(d => NcCandidateMapper.ToElection(d, d == latest, sourceUrl)).OrderBy(e => e.ElectionDate).ToList();
    }

    private static bool MatchesElectionDate(Dictionary<string, string> row, Election election, string[] dateFormats) =>
        DateParsing.TryParseAny(row.GetValueOrDefault("election_dt", ""), dateFormats, out var date) && date == election.ElectionDate;

    /// <summary>
    /// A (contest, candidate) pair that appears under more than one county is a
    /// statewide/multi-county race repeating per county's ballot - dedupe it
    /// into one county-less row. A pair under exactly one county is a genuinely
    /// local race and is left to CollectCountyBallots instead, not duplicated
    /// here.
    /// </summary>
    private static void CollectStatewideCandidates(
        List<Dictionary<string, string>> electionRows, Election election, string sourceUrl, CollectResult result)
    {
        var groups = electionRows.GroupBy(r => (
            Contest: r.GetValueOrDefault("contest_name", ""),
            Name: r.GetValueOrDefault("name_on_ballot", "")));

        foreach (var group in groups)
        {
            var counties = group.Select(r => r.GetValueOrDefault("county_name", "")).Distinct().Count();
            if (counties <= 1)
                continue;

            var candidate = NcCandidateMapper.ToCandidateRow(group.First(), election, sourceUrl, county: null);
            RowHelpers.StampState(candidate, "NC");
            result.Candidates.Add(candidate);
        }
    }

    private static void CollectCountyBallots(
        List<Dictionary<string, string>> electionRows, Election election, string sourceUrl, CollectResult result)
    {
        foreach (var countyGroup in electionRows.GroupBy(r => r.GetValueOrDefault("county_name", "")))
        {
            if (countyGroup.Key.Length == 0)
                continue;

            var ballot = new CountyBallot
            {
                State = "NC",
                CountyName = countyGroup.Key,
                ElectionDate = election.ElectionDate.ToString("yyyy-MM-dd"),
                ElectionType = election.ElectionType,
                SourceUrl = sourceUrl,
            };
            foreach (var row in countyGroup)
                ballot.Candidates.Add(NcCandidateMapper.ToCandidateRow(row, election, sourceUrl, ballot.CountyName));

            ballot.Candidates.Sort(CollectResultSorter.CompareCandidates);
            result.CountyBallots.Add(ballot);
        }
    }

    private void BuildSourcesManifest(CollectResult result, string url)
    {
        var sources = result.Sources;
        sources.Elections = [new SourceEntry(url, "csv")];
        sources.StatewideCandidates = [new SourceEntry(url, "csv")];
        sources.CountyBallots = result.CountyBallots
            .Select(b => b.CountyName)
            .Distinct()
            .ToDictionary(c => c, _ => new List<SourceEntry> { new(url, "csv") }, StringComparer.Ordinal);
        sources.VerificationOnly = [new SourceEntry($"https://ballotpedia.org/North_Carolina_elections,_{_year}", "html")];
        sources.NextRun = _schedule.Recommend(result, _year);
    }
}
