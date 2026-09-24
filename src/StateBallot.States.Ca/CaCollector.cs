using System.Text.Json;
using StateBallot.Core;
using StateBallot.Core.Raw;

namespace StateBallot.States.Ca;

/// <summary>California state collector, backed by sos.ca.gov and elections.cdn.sos.ca.gov.</summary>
[StateCode("CA")]
public sealed class CaCollector : IStateCollector
{
    public const string CertifiedListRole = "certified-list";

    private readonly CaSourceConfig _config;
    private readonly IPublishSchedule _schedule;
    private readonly int _year;
    private readonly string _stateDataDir;
    private readonly string _inputDataRoot;

    // Certified-list source per collected election, for the provenance manifest.
    private readonly Dictionary<string, string> _certifiedListUrls = new();

    public string StateCode => "CA";

    /// <param name="stateDataDir">Per-state output directory (e.g. data/output/ca or state-roster-data/ca).</param>
    /// <param name="inputDataRoot">Pipeline data root containing input/. Inferred from data/output/&lt;xx&gt; when null.</param>
    public CaCollector(
        int year,
        string stateDataDir,
        string? inputDataRoot = null,
        CaSourceConfig? config = null)
    {
        _year = year;
        _stateDataDir = stateDataDir;
        _inputDataRoot = ResolveInputDataRoot(stateDataDir, inputDataRoot);
        _config = config ?? new CaSourceConfig();
        _schedule = new CaPublishSchedule();
    }

    /// <summary>
    /// A special election's primary and general share one detail page and so one election id,
    /// so the date is part of what identifies its captured pages.
    /// </summary>
    public static (string Key, string Value)[] ElectionKeys(Election election) =>
        [("election", election.ElectionId), ("date", election.ElectionDate.ToString("yyyy-MM-dd"))];

    public async Task CaptureAsync(HttpFetcher fetcher, DateOnly asOf)
    {
        Console.WriteLine($"Capturing California sources for {_year}...");

        var allElections = await new UpcomingElectionsScraper(_config).CaptureAsync(fetcher);
        foreach (var election in ElectionFilters.ForTargetYear(allElections, _year, asOf))
        {
            Console.WriteLine($"  {election.Name} ({election.ElectionDate:MM/dd/yyyy})...");
            var url = election.Jurisdiction == "state"
                ? StatewideCertifiedListUrl(election)
                : await SpecialElectionPageScraper.CaptureCertifiedListUrlAsync(fetcher, election);
            if (url is not null)
                await fetcher.TryGetBytesAsync(url, FetchTag.Of(CertifiedListRole, ElectionKeys(election)));
        }

        await new QualifiedMeasuresScraper(_config).CaptureAsync(fetcher);
        await new CountyDirectoryScraper(_config).CaptureAsync(fetcher);
        await new CountyElectionsScraper(_config).CaptureAsync(fetcher);
    }

    public CollectResult Normalize(CaptureReader capture)
    {
        Console.WriteLine($"Normalizing California capture {capture.CaptureId} for {_year}...");

        var fipsPath = DataPaths.CountyFipsPath(_inputDataRoot, StateCode);
        if (!File.Exists(fipsPath))
            throw new InvalidOperationException($"County FIPS data file not found at {fipsPath}.");
        var fips = JsonSerializer.Deserialize<SortedDictionary<string, string>>(File.ReadAllText(fipsPath))
                   ?? throw new InvalidOperationException($"County FIPS data file {fipsPath} is empty.");

        var result = new CollectResult
        {
            CountyCodes = new SortedDictionary<string, string>(
                fips.ToDictionary(kv => kv.Value, kv => kv.Key), StringComparer.Ordinal),
        };

        var allElections = new UpcomingElectionsScraper(_config).Parse(capture.Require(UpcomingElectionsScraper.Role).Text());
        Console.WriteLine($"  Elections listed on the SoS upcoming-elections page: {allElections.Count}");

        var targetElections = ElectionFilters.ForTargetYear(allElections, _year, capture.AsOf);
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
            var candidates = CollectCandidates(election, result, capture);
            if (candidates is not null)
                candidatesByElection[election.ElectionId] = candidates;
        }

        foreach (var candidates in candidatesByElection.Values)
            result.Candidates.AddRange(candidates);

        var measures = new QualifiedMeasuresScraper(_config).Parse(capture.Require(QualifiedMeasuresScraper.Role).Text());
        foreach (var measure in measures)
        {
            var date = DateOnly.Parse(measure.ElectionDate!);
            if (!ElectionFilters.InTargetYear(date, _year, capture.AsOf))
                continue;
            RowHelpers.StampState(measure, StateCode);
            result.StatewideProposedMeasures.Add(measure);
        }
        Console.WriteLine($"  Qualified statewide measures: {result.StatewideProposedMeasures.Count}");

        var directory = new CountyDirectoryScraper(_config).Parse(capture.Require(CountyDirectoryScraper.Role).Text(), fipsPath);
        RowHelpers.StampState(directory, StateCode);
        result.CountyDirectory.AddRange(directory);
        Console.WriteLine($"  Counties in directory: {result.CountyDirectory.Count}");

        var countyEntries = new CountyElectionsScraper(_config).Parse(capture.Require(CountyElectionsScraper.Role).Text(), fips.Keys);
        ProcessCountyElections(countyEntries, targetElections, candidatesByElection, result, capture.AsOf);

        if (result.Candidates.Count == 0 && result.PendingElections.Count == result.Elections.Count)
            Console.WriteLine("  Note: no election has a published candidate list yet; see gaps.");

        CollectResultSorter.Sort(result);
        BuildSourcesManifest(result);
        return result;
    }

    private string StatewideCertifiedListUrl(Election election)
    {
        var kind = election.ElectionType.Contains("Primary", StringComparison.OrdinalIgnoreCase)
            ? "primary" : "general";
        return _config.CertifiedListUrl(_year, kind);
    }

    private List<CandidateRow>? CollectCandidates(Election election, CollectResult result, CaptureReader capture)
    {
        string? url;
        if (election.Jurisdiction == "state")
        {
            url = StatewideCertifiedListUrl(election);
        }
        else
        {
            var page = capture.Require(SpecialElectionPageScraper.Role, ElectionKeys(election));
            url = SpecialElectionPageScraper.FindCertifiedListUrl(page.Text(), election.SourceUrl, election.ElectionDate);
            if (url is null)
            {
                result.Gaps.Add(
                    $"{election.Name}: no 'Certified List of Candidates' link found on {election.SourceUrl} " +
                    "for this election date. The list may not be certified yet; re-run later.");
                result.PendingElections.Add(election);
                return null;
            }
        }

        var certifiedList = capture.Require(CertifiedListRole, ElectionKeys(election));
        if (!certifiedList.HasPayload)
        {
            result.Gaps.Add(
                $"{election.Name} ({election.ElectionDate:yyyy-MM-dd}): certified candidate list is not posted yet at {url}. " +
                $"California posts it 68 days before election day ({election.ElectionDate.AddDays(-68):yyyy-MM-dd}); re-run after that.");
            result.PendingElections.Add(election);
            return null;
        }

        var candidates = CertifiedListPdfParser.Parse(certifiedList.Bytes(), url);
        foreach (var candidate in candidates)
        {
            RowHelpers.StampState(candidate, StateCode);
            candidate.ElectionDate = election.ElectionDate.ToString("yyyy-MM-dd");
            candidate.ElectionType = election.ElectionType;
            candidate.SourceElectionId = election.ElectionId;
        }

        _certifiedListUrls[election.ElectionId] = url;
        Console.WriteLine($"    {candidates.Count} candidates from certified list ({url})");
        return candidates;
    }

    private void ProcessCountyElections(
        List<CountyElectionEntry> entries,
        List<Election> targetElections,
        Dictionary<string, List<CandidateRow>> candidatesByElection,
        CollectResult result,
        DateOnly asOf)
    {
        foreach (var entry in entries)
        {
            if (!ElectionFilters.InTargetYear(entry.Date, _year, asOf))
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
            new SourceEntry("data/input/ca/county_fips.json (U.S. Census county FIPS codes)", "json"),
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
