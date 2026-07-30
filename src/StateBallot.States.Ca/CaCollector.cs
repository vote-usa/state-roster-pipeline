using System.Text.Json;
using StateBallot.Core;

namespace StateBallot.States.Ca;

/// <summary>California state collector, backed by sos.ca.gov and elections.cdn.sos.ca.gov.</summary>
[StateCode("CA")]
public sealed class CaCollector : IStateCollector
{
    private readonly HttpFetcher _fetcher;
    private readonly CaSourceConfig _config;
    private readonly IPublishSchedule _schedule;
    private readonly int _year;
    private readonly string _stateDataDir;

    // Certified-list source per collected election, for the provenance manifest.
    private readonly Dictionary<string, string> _certifiedListUrls = new();

    public string StateCode => "CA";

    /// <param name="stateDataDir">Per-state data directory holding county_fips.json.</param>
    public CaCollector(HttpFetcher fetcher, int year, string stateDataDir, CaSourceConfig? config = null)
    {
        _fetcher = fetcher;
        _year = year;
        _stateDataDir = stateDataDir;
        _config = config ?? new CaSourceConfig();
        _schedule = new CaPublishSchedule();
    }

    public async Task<CollectResult> CollectAsync()
    {
        Console.WriteLine($"Collecting California ballot roster for {_year}...");

        var fipsPath = Path.Combine(_stateDataDir, "county_fips.json");
        if (!File.Exists(fipsPath))
            throw new InvalidOperationException($"County FIPS data file not found at {fipsPath}.");
        var fips = JsonSerializer.Deserialize<SortedDictionary<string, string>>(File.ReadAllText(fipsPath))
                   ?? throw new InvalidOperationException($"County FIPS data file {fipsPath} is empty.");

        var result = new CollectResult
        {
            CountyCodes = new SortedDictionary<string, string>(
                fips.ToDictionary(kv => kv.Value, kv => kv.Key), StringComparer.Ordinal),
        };

        var allElections = await new UpcomingElectionsScraper(_fetcher, _config).FetchAsync();
        Console.WriteLine($"  Elections listed on the SoS upcoming-elections page: {allElections.Count}");

        var targetElections = ElectionFilters.ForTargetYear(allElections, _year);
        if (targetElections.Count == 0)
            throw new InvalidOperationException(
                $"No upcoming elections found for {_year} at {_config.UpcomingElectionsUrl}. " +
                "If this is early in the year the page may not list the year's elections yet.");

        RowHelpers.StampState(targetElections, StateCode);
        result.Elections.AddRange(targetElections);

        var candidatesByElection = new Dictionary<string, List<CandidateRow>>();
        foreach (var election in targetElections)
        {
            Console.WriteLine($"  {election.Name} ({election.ElectionDate:MM/dd/yyyy})...");
            var candidates = await CollectCandidatesAsync(election, result);
            if (candidates is not null)
                candidatesByElection[election.ElectionId] = candidates;
        }

        foreach (var candidates in candidatesByElection.Values)
            result.Candidates.AddRange(candidates);

        var measures = await new QualifiedMeasuresScraper(_fetcher, _config).FetchAsync();
        foreach (var measure in measures)
        {
            var date = DateOnly.Parse(measure.ElectionDate!);
            if (!ElectionFilters.InTargetYear(date, _year))
                continue;
            RowHelpers.StampState(measure, StateCode);
            result.StatewideProposedMeasures.Add(measure);
        }
        Console.WriteLine($"  Qualified statewide measures: {result.StatewideProposedMeasures.Count}");

        var directory = await new CountyDirectoryScraper(_fetcher, _config).FetchAsync(fipsPath);
        RowHelpers.StampState(directory, StateCode);
        result.CountyDirectory.AddRange(directory);
        Console.WriteLine($"  Counties in directory: {result.CountyDirectory.Count}");

        var countyEntries = await new CountyElectionsScraper(_fetcher, _config).FetchAsync(fips.Keys);
        ProcessCountyElections(countyEntries, targetElections, candidatesByElection, result);

        if (result.Candidates.Count == 0 && result.PendingElections.Count == result.Elections.Count)
            Console.WriteLine("  Note: no election has a published candidate list yet; see gaps.");

        CollectResultSorter.Sort(result);
        BuildSourcesManifest(result);
        return result;
    }

    private async Task<List<CandidateRow>?> CollectCandidatesAsync(Election election, CollectResult result)
    {
        string? url;
        if (election.Jurisdiction == "state")
        {
            var kind = election.ElectionType.Contains("Primary", StringComparison.OrdinalIgnoreCase)
                ? "primary" : "general";
            url = _config.CertifiedListUrl(_year, kind);
        }
        else
        {
            url = await new SpecialElectionPageScraper(_fetcher)
                .FindCertifiedListUrlAsync(election.SourceUrl, election.ElectionDate);
            if (url is null)
            {
                result.Gaps.Add(
                    $"{election.Name}: no 'Certified List of Candidates' link found on {election.SourceUrl} " +
                    "for this election date. The list may not be certified yet; re-run later.");
                result.PendingElections.Add(election);
                return null;
            }
        }

        var pdfBytes = await _fetcher.TryGetBytesAsync(url);
        if (pdfBytes is null)
        {
            result.Gaps.Add(
                $"{election.Name} ({election.ElectionDate:yyyy-MM-dd}): certified candidate list is not posted yet at {url}. " +
                $"California posts it 68 days before election day ({election.ElectionDate.AddDays(-68):yyyy-MM-dd}); re-run after that.");
            result.PendingElections.Add(election);
            return null;
        }

        var candidates = CertifiedListPdfParser.Parse(pdfBytes, url);
        foreach (var candidate in candidates)
        {
            RowHelpers.StampState(candidate, StateCode);
            candidate.ElectionDate = election.ElectionDate.ToString("yyyy-MM-dd");
            candidate.ElectionType = election.ElectionType;
        }

        _certifiedListUrls[election.ElectionId] = url;
        Console.WriteLine($"    {candidates.Count} candidates from certified list ({url})");
        return candidates;
    }

    private void ProcessCountyElections(
        List<CountyElectionEntry> entries,
        List<Election> targetElections,
        Dictionary<string, List<CandidateRow>> candidatesByElection,
        CollectResult result)
    {
        foreach (var entry in entries)
        {
            if (!ElectionFilters.InTargetYear(entry.Date, _year))
                continue;

            var tracked = targetElections.FirstOrDefault(e =>
                (entry.DetailUrl is not null && UrlsMatch(entry.DetailUrl, e.SourceUrl)) ||
                (e.ElectionDate == entry.Date && NamesMatch(entry.Name, e.Name)));

            if (tracked is null)
            {
                result.Elections.Add(new Election
                {
                    State = StateCode,
                    ElectionId = $"{Slug(entry.CountyName)}-{entry.Date:yyyy-MM-dd}",
                    Name = $"{entry.CountyName} County - {entry.Name}",
                    ElectionDate = entry.Date,
                    ElectionType = UpcomingElectionsScraper.InferElectionType(entry.Name),
                    Jurisdiction = $"{entry.CountyName} County",
                    SourceUrl = _config.CountyAdministeredElectionsUrl,
                });
                result.Gaps.Add(
                    $"{entry.CountyName} County - {entry.Name} ({entry.Date:yyyy-MM-dd}): the SoS lists this county-administered " +
                    $"election but does not publish its ballot content; see the county elections office site" +
                    (entry.CountyUrl is null ? "." : $" at {entry.CountyUrl}."));
                continue;
            }

            if (candidatesByElection.TryGetValue(tracked.ElectionId, out var candidates))
            {
                result.CountyBallots.Add(new CountyBallot
                {
                    State = StateCode,
                    CountyName = entry.CountyName,
                    ElectionDate = tracked.ElectionDate.ToString("yyyy-MM-dd"),
                    ElectionType = tracked.ElectionType,
                    Candidates = candidates.Select(c => c.WithCounty(entry.CountyName)).ToList(),
                    SourceUrl = _config.CountyAdministeredElectionsUrl,
                });
            }
        }
    }

    private static bool UrlsMatch(string a, string b) =>
        string.Equals(a.TrimEnd('/'), b.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);

    private static bool NamesMatch(string a, string b) =>
        string.Equals(Slug(a), Slug(b), StringComparison.OrdinalIgnoreCase);

    private static string Slug(string text) =>
        new(text.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    private void BuildSourcesManifest(CollectResult result)
    {
        var sources = result.Sources;
        sources.Elections =
        [
            new SourceEntry(_config.UpcomingElectionsUrl, "html"),
            new SourceEntry(_config.CountyAdministeredElectionsUrl, "html"),
        ];
        sources.StatewideCandidates = result.Elections
            .Where(e => _certifiedListUrls.ContainsKey(e.ElectionId))
            .Select(e => new SourceEntry(_certifiedListUrls[e.ElectionId], "pdf"))
            .ToList();
        sources.StatewideMeasures = [new SourceEntry(_config.QualifiedMeasuresUrl, "html")];
        sources.CountyDirectory =
        [
            new SourceEntry(_config.CountyElectionsOfficesUrl, "html"),
            new SourceEntry("data/ca/county_fips.json (U.S. Census county FIPS codes)", "json"),
        ];
        sources.CountyBallots = result.CountyBallots
            .GroupBy(b => b.CountyName)
            .ToDictionary(
                g => g.Key,
                g => g.Select(b => new SourceEntry(b.SourceUrl, "html"))
                    .DistinctBy(e => e.Url)
                    .ToList(),
                StringComparer.Ordinal);
        sources.VerificationOnly =
        [
            new SourceEntry($"https://ballotpedia.org/California_elections,_{_year}", "html"),
        ];
        sources.NextRun = _schedule.Recommend(result, _year);
    }
}
