using System.Text.Json;
using StateBallot.Core;

namespace StateBallot.States.Ca;

/// <summary>California state collector, backed by sos.ca.gov and elections.cdn.sos.ca.gov.</summary>
public sealed class CaCollector : IStateCollector
{
    private readonly HttpFetcher _fetcher;
    private readonly CaSourceConfig _config;
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

        // 1. Statewide + special vacancy elections from the upcoming-elections page.
        var allElections = await new UpcomingElectionsScraper(_fetcher, _config).FetchAsync();
        Console.WriteLine($"  Elections listed on the SoS upcoming-elections page: {allElections.Count}");

        var targetElections = FilterUpcoming(allElections);
        if (targetElections.Count == 0)
            throw new InvalidOperationException(
                $"No upcoming elections found for {_year} at {_config.UpcomingElectionsUrl}. " +
                "If this is early in the year the page may not list the year's elections yet.");

        foreach (var election in targetElections)
        {
            election.State = StateCode;
            result.Elections.Add(election);
        }

        // 2. Candidates: certified list PDF per election (statewide lists post on
        // a statutory schedule; special-election lists live on their detail page).
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

        // 3. Qualified statewide ballot measures.
        var measures = await new QualifiedMeasuresScraper(_fetcher, _config).FetchAsync();
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        foreach (var measure in measures)
        {
            var date = DateOnly.Parse(measure.ElectionDate!);
            if (date.Year != _year || (_year == today.Year && date < today))
                continue;
            measure.State = StateCode;
            result.StatewideProposedMeasures.Add(measure);
        }
        Console.WriteLine($"  Qualified statewide measures: {result.StatewideProposedMeasures.Count}");

        // 4. County elections office directory (all 58 counties).
        foreach (var row in await new CountyDirectoryScraper(_fetcher, _config).FetchAsync(fipsPath))
        {
            row.State = StateCode;
            result.CountyDirectory.Add(row);
        }
        Console.WriteLine($"  Counties in directory: {result.CountyDirectory.Count}");

        // 5. County-administered (local) elections.
        var countyEntries = await new CountyElectionsScraper(_fetcher, _config).FetchAsync(fips.Keys);
        ProcessCountyElections(countyEntries, targetElections, candidatesByElection, result);

        if (result.Candidates.Count == 0 && result.PendingElections.Count == result.Elections.Count)
            Console.WriteLine("  Note: no election has a published candidate list yet; see gaps.");

        SortForDeterminism(result);
        BuildSourcesManifest(result);
        return result;
    }

    /// <summary>"Upcoming" = today or later within the target year; a non-current year collects the whole year.</summary>
    private List<Election> FilterUpcoming(List<Election> elections)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        return elections
            .Where(e => e.ElectionDate.Year == _year)
            .Where(e => _year != today.Year || e.ElectionDate >= today)
            .OrderBy(e => e.ElectionDate)
            .ThenBy(e => e.ElectionId, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// Fetches and parses the certified candidate list for one election.
    /// Returns null (and records a gap) when the list is not published yet.
    /// </summary>
    private async Task<List<CandidateRow>?> CollectCandidatesAsync(Election election, CollectResult result)
    {
        string? url;
        if (election.Jurisdiction == "state")
        {
            // Statewide elections use the year-templated CDN path. The certified
            // list posts 68 days before election day (Elections Code s. 8148).
            var kind = election.ElectionType.Contains("Primary", StringComparison.OrdinalIgnoreCase)
                ? "primary" : "general";
            url = _config.CertifiedListUrl(_year, kind);
        }
        else
        {
            // Special vacancy elections publish their certified list on the
            // election's detail page, in the section for this round's date.
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
            candidate.State = StateCode;
            candidate.ElectionDate = election.ElectionDate.ToString("yyyy-MM-dd");
            candidate.ElectionType = election.ElectionType;
        }

        _certifiedListUrls[election.ElectionId] = url;
        Console.WriteLine($"    {candidates.Count} candidates from certified list ({url})");
        return candidates;
    }

    /// <summary>
    /// Turns county-administered election listings into Election rows and county
    /// ballots. A county entry that links to an election we already collected
    /// statewide (e.g. a special vacancy election contained in one county) gets
    /// that election's candidates on the county's ballot.
    /// </summary>
    private void ProcessCountyElections(
        List<CountyElectionEntry> entries,
        List<Election> targetElections,
        Dictionary<string, List<CandidateRow>> candidatesByElection,
        CollectResult result)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        foreach (var entry in entries)
        {
            if (entry.Date.Year != _year || (_year == today.Year && entry.Date < today))
                continue;

            // Match to an already-tracked election by detail link or by date.
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
                    Candidates = candidates
                        .Select(c => CloneForCounty(c, entry.CountyName))
                        .OrderBy(c => c.Office, StringComparer.Ordinal)
                        .ThenBy(c => c.CandidateName, StringComparer.Ordinal)
                        .ToList(),
                    SourceUrl = _config.CountyAdministeredElectionsUrl,
                });
            }
        }
    }

    private static CandidateRow CloneForCounty(CandidateRow c, string countyName) => new()
    {
        State = c.State,
        ElectionDate = c.ElectionDate,
        ElectionType = c.ElectionType,
        Office = c.Office,
        District = c.District,
        County = countyName,
        CandidateName = c.CandidateName,
        Party = c.Party,
        Incumbent = c.Incumbent,
        SourceUrl = c.SourceUrl,
    };

    private static bool UrlsMatch(string a, string b) =>
        string.Equals(a.TrimEnd('/'), b.TrimEnd('/'), StringComparison.OrdinalIgnoreCase);

    /// <summary>Compares election names ignoring punctuation ("CD 14, Special..." vs "CD 14 Special...").</summary>
    private static bool NamesMatch(string a, string b) =>
        string.Equals(Slug(a), Slug(b), StringComparison.OrdinalIgnoreCase);

    private static string Slug(string text) =>
        new(text.ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    private static void SortForDeterminism(CollectResult result)
    {
        result.Elections.Sort((a, b) =>
        {
            var c = a.ElectionDate.CompareTo(b.ElectionDate);
            return c != 0 ? c : string.CompareOrdinal(a.ElectionId, b.ElectionId);
        });
        result.Candidates.Sort(CompareCandidates);
        result.StatewideProposedMeasures.Sort(CompareMeasures);
        result.CountyBallots.Sort((a, b) =>
        {
            var c = string.CompareOrdinal(a.CountyName, b.CountyName);
            return c != 0 ? c : string.CompareOrdinal(a.ElectionDate, b.ElectionDate);
        });
    }

    private static int CompareCandidates(CandidateRow a, CandidateRow b)
    {
        var c = string.CompareOrdinal(a.ElectionDate, b.ElectionDate);
        if (c != 0) return c;
        c = string.CompareOrdinal(a.Office, b.Office);
        if (c != 0) return c;
        c = PadDistrict(a.District).CompareTo(PadDistrict(b.District));
        if (c != 0) return c;
        return string.CompareOrdinal(a.CandidateName, b.CandidateName);
    }

    private static int PadDistrict(string? district) =>
        int.TryParse(district, out var n) ? n : 0;

    private static int CompareMeasures(MeasureRow a, MeasureRow b)
    {
        var c = string.CompareOrdinal(a.ElectionDate ?? "", b.ElectionDate ?? "");
        if (c != 0) return c;
        // "Proposition 2" before "Proposition 37".
        var aNum = int.TryParse(new string(a.MeasureId.Where(char.IsDigit).ToArray()), out var an) ? an : 0;
        var bNum = int.TryParse(new string(b.MeasureId.Where(char.IsDigit).ToArray()), out var bn) ? bn : 0;
        if (aNum != bNum) return aNum.CompareTo(bNum);
        return string.CompareOrdinal(a.MeasureId, b.MeasureId);
    }

    private void BuildSourcesManifest(CollectResult result)
    {
        result.SourceGroups["elections"] = new object[]
        {
            new { url = _config.UpcomingElectionsUrl, format = "html" },
            new { url = _config.CountyAdministeredElectionsUrl, format = "html" },
        };
        result.SourceGroups["statewide_candidates"] = result.Elections
            .Where(e => _certifiedListUrls.ContainsKey(e.ElectionId))
            .Select(e => new { url = _certifiedListUrls[e.ElectionId], format = "pdf" })
            .ToArray();
        result.SourceGroups["statewide_measures"] = new[]
        {
            new { url = _config.QualifiedMeasuresUrl, format = "html" },
        };
        result.SourceGroups["county_directory"] = new object[]
        {
            new { url = _config.CountyElectionsOfficesUrl, format = "html" },
            new { url = "data/ca/county_fips.json (U.S. Census county FIPS codes)", format = "json" },
        };
        result.SourceGroups["county_ballots"] = result.CountyBallots
            .GroupBy(b => b.CountyName)
            .ToDictionary(
                g => g.Key,
                g => g.Select(b => new { url = b.SourceUrl, format = "html" }).Distinct().ToArray());
        result.SourceGroups["verification_only"] = new[]
        {
            new { url = $"https://ballotpedia.org/California_elections,_{_year}", format = "html" },
        };

        result.NextRun = ComputeNextRun(result);
    }

    private object ComputeNextRun(CollectResult result)
    {
        // California compiles the certified list of candidates no later than the
        // 68th day before a statewide election (Elections Code s. 8148), so the
        // earliest useful re-run for a pending election is 67 days before it.
        if (result.PendingElections.Count > 0)
        {
            var earliestPending = result.PendingElections.MinBy(e => e.ElectionDate)!;
            var recommendedAfter = earliestPending.ElectionDate.AddDays(-67);

            return new
            {
                recommended_after = recommendedAfter.ToString("yyyy-MM-dd"),
                reason =
                    $"{earliestPending.Name} candidate list is not posted yet. California's Secretary of State " +
                    $"certifies the list of candidates 68 days before election day (Elections Code s. 8148), " +
                    $"i.e. by {earliestPending.ElectionDate.AddDays(-68):yyyy-MM-dd} for this election.",
                next_election_date = earliestPending.ElectionDate.ToString("yyyy-MM-dd"),
                next_election_type = earliestPending.ElectionType,
            };
        }

        var nextYear = _year + 1;
        return new
        {
            recommended_after = $"{nextYear}-01-15",
            reason = $"All {_year} elections collected. Check early {nextYear} for newly proclaimed special " +
                     "elections and the year's county-administered election calendar; statewide primary " +
                     "candidate lists post 68 days before the primary.",
            next_election_date = (string?)null,
            next_election_type = (string?)null,
        };
    }
}
