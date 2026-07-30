using StateBallot.Core;

namespace StateBallot.States.Wa;

/// <summary>Washington state collector, backed by VoteWA and sos.wa.gov.</summary>
[StateCode("WA")]
public sealed class WaCollector : IStateCollector
{
    private static readonly string[] StatewideCategoryNames =
        { "Federal Candidates", "Legislative Candidates", "Judicial Candidates", "State Candidates", "Statewide Candidates" };

    private readonly HttpFetcher _fetcher;
    private readonly WaSourceConfig _config;
    private readonly IPublishSchedule _schedule;
    private readonly int _year;
    private readonly string _stateDataDir;

    public string StateCode => "WA";

    /// <param name="stateDataDir">Per-state output directory (data/output/&lt;xx&gt;/). Inputs are under data/input/&lt;xx&gt;/.</param>
    public WaCollector(HttpFetcher fetcher, int year, string stateDataDir, WaSourceConfig? config = null)
    {
        _fetcher = fetcher;
        _year = year;
        _stateDataDir = stateDataDir;
        _config = config ?? new WaSourceConfig();
        _schedule = new WaPublishSchedule();
    }

    public async Task<CollectResult> CollectAsync()
    {
        Console.WriteLine($"Collecting Washington ballot roster for {_year}...");

        var electionScraper = new ElectionListScraper(_fetcher, _config);
        var (allElections, countyCodes) = await electionScraper.FetchAsync();
        Console.WriteLine($"  Elections listed on VoteWA: {allElections.Count}; counties: {countyCodes.Count}");

        var electionsInYear = allElections.Where(e => e.ElectionDate.Year == _year).ToList();
        if (electionsInYear.Count == 0)
            throw new InvalidOperationException(
                $"No elections found for {_year} in the VoteWA election dropdown. " +
                "If this is early in the year the dropdown may not list the year's elections yet.");

        var targetElections = ElectionFilters.ForTargetYear(allElections, _year);

        var result = new CollectResult { CountyCodes = countyCodes };

        if (targetElections.Count == 0)
        {
            // Elections exist for the year, they've just all already happened as of
            // today - not a broken source, nothing to fail loudly about.
            result.Gaps.Add(
                $"All {electionsInYear.Count} election(s) listed for {_year} have already passed as of " +
                $"{DateOnly.FromDateTime(DateTime.UtcNow.Date):yyyy-MM-dd}; nothing upcoming to collect this run.");
        }

        RowHelpers.StampState(targetElections, StateCode);
        result.Elections.AddRange(targetElections);

        var guideClient = new VoterGuideClient(_fetcher, _config);
        // Nothing to attempt when there are no target elections - don't treat that
        // as a fetch failure (that's the case just handled above via Gaps).
        var anyGuideData = targetElections.Count == 0;

        foreach (var election in targetElections)
        {
            Console.WriteLine($"  {election.Name} ({election.ElectionDate:MM/dd/yyyy})...");
            var statewideGuide = await guideClient.FetchGuideAsync(election.ElectionId);

            if (statewideGuide.Categories.Count == 0)
            {
                result.Gaps.Add(
                    $"{election.Name} ({election.ElectionDate:yyyy-MM-dd}): VoteWA voters' guide is not published yet " +
                    "(candidates/measures are certified closer to the election). Re-run later to fill in this election.");
                result.PendingElections.Add(election);
                continue;
            }
            anyGuideData = true;

            // Map RaceID -> counties whose filtered guide includes it, so local
            // races can be attributed to counties.
            var raceCounties = new Dictionary<string, SortedSet<string>>();
            foreach (var (code, countyName) in countyCodes)
            {
                var countyGuide = await guideClient.FetchGuideAsync(election.ElectionId, code);
                var ballot = BuildCountyBallot(election, countyName, code, countyGuide);
                if (ballot.Candidates.Count > 0 || ballot.Measures.Count > 0)
                    result.CountyBallots.Add(ballot);

                foreach (var race in countyGuide.Categories.SelectMany(c => c.Races))
                {
                    if (race.RaceID is null) continue;
                    if (!raceCounties.TryGetValue(race.RaceID, out var set))
                        raceCounties[race.RaceID] = set = new SortedSet<string>(StringComparer.Ordinal);
                    set.Add(countyName);
                }
            }

            foreach (var category in statewideGuide.Categories)
            {
                var isMeasureCategory = string.Equals(category.Name?.Trim(), "Measures", StringComparison.OrdinalIgnoreCase);
                var isStatewideCategory = StatewideCategoryNames.Contains(category.Name?.Trim(), StringComparer.OrdinalIgnoreCase);

                foreach (var race in category.Races)
                {
                    var county = AttributeCounty(race, raceCounties, isStatewideCategory);
                    if (isMeasureCategory)
                    {
                        result.Measures.Add(ToMeasureRow(election, race, county));
                    }
                    else
                    {
                        foreach (var candidate in race.Candidates.Where(c => !string.IsNullOrWhiteSpace(c.BallotName)))
                            result.Candidates.Add(ToCandidateRow(election, race, candidate, county));
                    }
                }
            }
        }

        if (!anyGuideData)
            throw new InvalidOperationException(
                "Every upcoming election's VoteWA voters' guide was empty; refusing to write hollow outputs. " +
                "Check https://voter.votewa.gov manually.");

        // Statewide proposed measures from the SoS page (not yet certified to a ballot).
        var measuresScraper = new StatewideMeasuresScraper(_fetcher, _config);
        try
        {
            foreach (var measure in await measuresScraper.FetchAsync(_year))
            {
                RowHelpers.StampState(measure, StateCode);
                result.StatewideProposedMeasures.Add(measure);
            }
        }
        catch (InvalidOperationException ex)
        {
            result.Gaps.Add($"Statewide measures: {ex.Message}");
        }

        var directoryScraper = new CountyDirectoryScraper(_fetcher, _config);
        var (dataRoot, _) = DataPaths.FromStateOutputDir(_stateDataDir);
        var fipsPath = DataPaths.CountyFipsPath(dataRoot, StateCode);
        var directory = await directoryScraper.FetchAsync(countyCodes.Values.ToList(), fipsPath);
        RowHelpers.StampState(directory, StateCode);
        result.CountyDirectory.AddRange(directory);

        CollectResultSorter.Sort(result);
        BuildSourcesManifest(result);
        return result;
    }

    private CountyBallot BuildCountyBallot(Election election, string countyName, string countyCode, GuideResponse guide)
    {
        var ballot = new CountyBallot
        {
            State = StateCode,
            CountyName = countyName,
            ElectionDate = election.ElectionDate.ToString("yyyy-MM-dd"),
            ElectionType = election.ElectionType,
            SourceUrl = _config.VoterGuideUrl(election.ElectionId, countyCode),
        };

        foreach (var category in guide.Categories)
        {
            var isMeasureCategory = string.Equals(category.Name?.Trim(), "Measures", StringComparison.OrdinalIgnoreCase);
            foreach (var race in category.Races)
            {
                if (isMeasureCategory)
                    ballot.Measures.Add(ToMeasureRow(election, race, countyName));
                else
                    foreach (var candidate in race.Candidates.Where(c => !string.IsNullOrWhiteSpace(c.BallotName)))
                        ballot.Candidates.Add(ToCandidateRow(election, race, candidate, countyName));
            }
        }

        ballot.Candidates.Sort(CollectResultSorter.CompareCandidates);
        ballot.Measures.Sort(CollectResultSorter.CompareMeasures);
        return ballot;
    }

    private CandidateRow ToCandidateRow(Election election, GuideRace race, GuideCandidate candidate, string? county) => new()
    {
        State = StateCode,
        ElectionDate = election.ElectionDate.ToString("yyyy-MM-dd"),
        ElectionType = election.ElectionType,
        Office = race.Name?.Trim() ?? "",
        District = string.IsNullOrWhiteSpace(race.Jurisdiction) ? null : race.Jurisdiction.Trim(),
        County = county,
        CandidateName = candidate.BallotName!.Trim(),
        Party = VoterGuideClient.NormalizeParty(candidate.PartyName),
        Incumbent = null, // not published by VoteWA
        SourceUrl = _config.VoterGuideUrl(election.ElectionId),
    };

    private MeasureRow ToMeasureRow(Election election, GuideRace race, string? county) => new()
    {
        State = StateCode,
        ElectionDate = election.ElectionDate.ToString("yyyy-MM-dd"),
        MeasureId = race.RaceID ?? "",
        Title = (race.BallotTitle ?? race.MeasureName ?? race.Name ?? "").Trim(),
        Summary = VoterGuideClient.StripHtml(race.ShortDescription),
        FullTextUrl = null,
        Jurisdiction = string.IsNullOrWhiteSpace(race.Jurisdiction) ? "local" : race.Jurisdiction.Trim(),
        County = county,
        SourceUrl = _config.VoterGuideUrl(election.ElectionId),
    };

    private static string? AttributeCounty(
        GuideRace race, Dictionary<string, SortedSet<string>> raceCounties, bool isStatewideCategory)
    {
        if (isStatewideCategory)
            return null;
        if (race.RaceID is not null && raceCounties.TryGetValue(race.RaceID, out var counties) && counties.Count > 0)
            return string.Join("; ", counties);
        return null;
    }

    private void BuildSourcesManifest(CollectResult result)
    {
        var publishedElections = result.Elections
            .Where(e => result.PendingElections.All(p => p.ElectionId != e.ElectionId))
            .ToList();

        var sources = result.Sources;
        sources.Elections = [new SourceEntry(_config.CandidateListUrl, "html")];
        sources.StatewideCandidates = publishedElections
            .Select(e => new SourceEntry(_config.VoterGuideUrl(e.ElectionId), "json"))
            .ToList();
        sources.StatewideMeasures = [new SourceEntry(_config.StatewideMeasuresUrl, "html")];
        sources.LocalMeasures = publishedElections
            .Select(e => new SourceEntry(_config.VoterGuideUrl(e.ElectionId), "json"))
            .ToList();
        sources.CountyDirectory =
        [
            new SourceEntry(_config.CountyElectionsOfficesUrl, "html"),
            new SourceEntry("data/input/wa/county_fips.json (U.S. Census county FIPS codes)", "json"),
        ];
        sources.CountyBallots = result.CountyCodes.ToDictionary(
            kv => kv.Value,
            kv => publishedElections
                .Select(e => new SourceEntry(_config.VoterGuideUrl(e.ElectionId, kv.Key), "json"))
                .ToList(),
            StringComparer.Ordinal);
        sources.VerificationOnly =
        [
            new SourceEntry("https://ballotpedia.org/Washington_elections,_" + _year, "html"),
        ];
        sources.NextRun = _schedule.Recommend(result, _year);
    }
}
