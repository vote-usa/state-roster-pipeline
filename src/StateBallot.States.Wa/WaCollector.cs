using StateBallot.Core;

namespace StateBallot.States.Wa;

/// <summary>Washington state collector, backed by VoteWA and sos.wa.gov.</summary>
public sealed class WaCollector : IStateCollector
{
    private static readonly string[] StatewideCategoryNames =
        { "Federal Candidates", "Legislative Candidates", "Judicial Candidates", "State Candidates", "Statewide Candidates" };

    private readonly HttpFetcher _fetcher;
    private readonly WaSourceConfig _config;
    private readonly int _year;
    private readonly string _stateDataDir;

    public string StateCode => "WA";

    /// <param name="stateDataDir">Per-state data directory holding county_fips.json.</param>
    public WaCollector(HttpFetcher fetcher, int year, string stateDataDir, WaSourceConfig? config = null)
    {
        _fetcher = fetcher;
        _year = year;
        _stateDataDir = stateDataDir;
        _config = config ?? new WaSourceConfig();
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

        // "Upcoming" = today or later within the target year. When back-filling a
        // different year, take the whole year.
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var targetElections = electionsInYear
            .Where(e => _year != today.Year || e.ElectionDate >= today)
            .OrderBy(e => e.ElectionDate)
            .ThenBy(e => e.ElectionId, StringComparer.Ordinal)
            .ToList();

        var result = new CollectResult { CountyCodes = countyCodes };

        if (targetElections.Count == 0)
        {
            // Elections exist for the year, they've just all already happened as of
            // today - not a broken source, nothing to fail loudly about.
            result.Gaps.Add(
                $"All {electionsInYear.Count} election(s) listed for {_year} have already passed as of " +
                $"{today:yyyy-MM-dd}; nothing upcoming to collect this run.");
        }

        foreach (var election in targetElections)
        {
            election.State = StateCode;
            result.Elections.Add(election);
        }

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
                            result.Candidates.Add(ToCandidateData(election, race, candidate, county));
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
                measure.State = StateCode;
                result.StatewideProposedMeasures.Add(measure);
            }
        }
        catch (InvalidOperationException ex)
        {
            result.Gaps.Add($"Statewide measures: {ex.Message}");
        }

        var directoryScraper = new CountyDirectoryScraper(_fetcher, _config);
        var fipsPath = Path.Combine(_stateDataDir, "county_fips.json");
        foreach (var row in await directoryScraper.FetchAsync(countyCodes.Values.ToList(), fipsPath))
        {
            row.State = StateCode;
            result.CountyDirectory.Add(row);
        }

        SortForDeterminism(result);
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
                        ballot.Candidates.Add(ToCandidateData(election, race, candidate, countyName));
            }
        }

        ballot.Candidates.Sort(CompareCandidates);
        ballot.Measures.Sort(CompareMeasures);
        return ballot;
    }

    private CandidateData ToCandidateData(Election election, GuideRace race, GuideCandidate candidate, string? county) => new()
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

    private static void SortForDeterminism(CollectResult result)
    {
        result.Candidates.Sort(CompareCandidates);
        result.Measures.Sort(CompareMeasures);
        result.CountyBallots.Sort((a, b) =>
        {
            var c = string.CompareOrdinal(a.CountyName, b.CountyName);
            return c != 0 ? c : string.CompareOrdinal(a.ElectionDate, b.ElectionDate);
        });
    }

    private static int CompareCandidates(CandidateData a, CandidateData b)
    {
        var c = string.CompareOrdinal(a.ElectionDate, b.ElectionDate);
        if (c != 0) return c;
        c = string.CompareOrdinal(a.District ?? "", b.District ?? "");
        if (c != 0) return c;
        c = string.CompareOrdinal(a.Office, b.Office);
        if (c != 0) return c;
        return string.CompareOrdinal(a.CandidateName, b.CandidateName);
    }

    private static int CompareMeasures(MeasureRow a, MeasureRow b)
    {
        var c = string.CompareOrdinal(a.ElectionDate ?? "", b.ElectionDate ?? "");
        if (c != 0) return c;
        c = string.CompareOrdinal(a.County ?? "", b.County ?? "");
        if (c != 0) return c;
        return string.CompareOrdinal(a.MeasureId, b.MeasureId);
    }

    private void BuildSourcesManifest(CollectResult result)
    {
        var publishedElections = result.Elections
            .Where(e => result.PendingElections.All(p => p.ElectionId != e.ElectionId))
            .ToList();

        result.SourceGroups["elections"] = new[]
        {
            new { url = _config.CandidateListUrl, format = "html" },
        };
        result.SourceGroups["statewide_candidates"] = publishedElections
            .Select(e => new { url = _config.VoterGuideUrl(e.ElectionId), format = "json" })
            .ToArray();
        result.SourceGroups["statewide_measures"] = new[]
        {
            new { url = _config.StatewideMeasuresUrl, format = "html" },
        };
        result.SourceGroups["local_measures"] = publishedElections
            .Select(e => new { url = _config.VoterGuideUrl(e.ElectionId), format = "json" })
            .ToArray();
        result.SourceGroups["county_directory"] = new object[]
        {
            new { url = _config.CountyElectionsOfficesUrl, format = "html" },
            new { url = "data/wa/county_fips.json (U.S. Census county FIPS codes)", format = "json" },
        };
        result.SourceGroups["county_ballots"] = result.CountyCodes.ToDictionary(
            kv => kv.Value,
            kv => publishedElections
                .Select(e => new { url = _config.VoterGuideUrl(e.ElectionId, kv.Key), format = "json" })
                .ToArray());
        result.SourceGroups["verification_only"] = new[]
        {
            new { url = "https://ballotpedia.org/Washington_elections,_" + _year, format = "html" },
        };

        result.NextRun = ComputeNextRun(result);
    }

    private object ComputeNextRun(CollectResult result)
    {
        // Washington certifies primary results and statewide measures roughly two and
        // a half weeks after the primary (RCW 29A.60.190/240); the online voters'
        // guide for the general is populated once that happens.
        if (result.PendingElections.Count > 0)
        {
            var earliestPending = result.PendingElections.MinBy(e => e.ElectionDate)!;
            var lastPublished = result.Elections
                .Where(e => result.PendingElections.All(p => p.ElectionId != e.ElectionId))
                .Where(e => e.ElectionDate < earliestPending.ElectionDate)
                .MaxBy(e => e.ElectionDate);

            var recommendedAfter = lastPublished is not null
                ? lastPublished.ElectionDate.AddDays(17)
                : earliestPending.ElectionDate.AddDays(-45);

            return new
            {
                recommended_after = recommendedAfter.ToString("yyyy-MM-dd"),
                reason = lastPublished is not null
                    ? $"{earliestPending.Name} ({earliestPending.ElectionDate:yyyy-MM-dd}) ballot data is not published yet. " +
                      $"Washington certifies the {lastPublished.Name} results and statewide measures about 17 days after election day, " +
                      "after which the VoteWA general-election guide and certified candidate list are populated."
                    : $"{earliestPending.Name} ballot data is typically published about 45 days before election day.",
                next_election_date = earliestPending.ElectionDate.ToString("yyyy-MM-dd"),
                next_election_type = earliestPending.ElectionType,
            };
        }

        var nextYear = _year + 1;

        if (result.Elections.Count == 0)
        {
            return new
            {
                recommended_after = $"{nextYear}-05-20",
                reason = $"All elections listed for {_year} have already passed; nothing was collected this run. " +
                         $"Washington's {nextYear} candidate filing week ends in mid-May; the VoteWA candidate list " +
                         "is populated within days of filing week closing.",
                next_election_date = (string?)null,
                next_election_type = (string?)null,
            };
        }

        return new
        {
            recommended_after = $"{nextYear}-05-20",
            reason = $"All {_year} elections collected. Washington's {nextYear} candidate filing week ends in mid-May; " +
                     "the VoteWA candidate list is populated within days of filing week closing.",
            next_election_date = (string?)null,
            next_election_type = (string?)null,
        };
    }
}
