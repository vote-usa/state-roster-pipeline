using StateBallot.Core;

namespace StateBallot.States.Tx;

/// <summary>Texas state collector, backed by the CivixApps CBP API.</summary>
public sealed class TxCollector : IStateCollector
{
    private readonly HttpFetcher _fetcher;
    private readonly TxSourceConfig _config;
    private readonly int _year;

    public string StateCode => "TX";

    // stateDataDir is unused here - accepted only to match Runner.cs's shared
    // IStateCollector factory signature (WA needs it for county_fips.json; TX doesn't
    // have any per-state reference data).
    public TxCollector(HttpFetcher fetcher, int year, string stateDataDir, TxSourceConfig? config = null)
    {
        _fetcher = fetcher;
        _year = year;
        _config = config ?? new TxSourceConfig();

        // Stamps Cloudflare-spoofing headers onto the fetcher for every request it
        // makes from now on - assumes one HttpFetcher per single-state run (true today
        // since Runner.cs builds a fresh one per invocation). Do not share this fetcher
        // instance with another state's collector.
        foreach (var (name, value) in TxSourceConfig.ExtraHeaders)
            _fetcher.AddDefaultHeader(name, value);
    }

    public async Task<CollectResult> CollectAsync()
    {
        Console.WriteLine($"Collecting Texas ballot roster for {_year}...");

        var electionClient = new TxElectionClient(_fetcher, _config);
        var candidateClient = new TxCandidateClient(_fetcher, _config);

        var rawElections = await electionClient.FetchElectionsByYearAsync(_year);
        Console.WriteLine($"  Elections listed for {_year}: {rawElections.Count}");

        if (rawElections.Count == 0)
            throw new InvalidOperationException(
                $"No elections found for {_year} via getElectionsByYear. " +
                "If this is early in the year the API may not list the year's elections yet.");

        var today = DateOnly.FromDateTime(DateTime.UtcNow.Date);
        var targetElections = rawElections
            .Select(TxCandidateMapper.ToElection)
            .Where(e => _year != today.Year || e.ElectionDate >= today)
            .OrderBy(e => e.ElectionDate)
            .ThenBy(e => e.ElectionId, StringComparer.Ordinal)
            .ToList();

        var result = new CollectResult();

        if (targetElections.Count == 0)
        {
            // Elections exist for the year, they've just all already happened as of
            // today - not a broken source, nothing to fail loudly about.
            result.Gaps.Add(
                $"All {rawElections.Count} election(s) listed for {_year} have already passed as of " +
                $"{today:yyyy-MM-dd}; nothing upcoming to collect this run.");
        }

        foreach (var election in targetElections)
            result.Elections.Add(election);

        // Nothing to attempt when there are no target elections - don't treat that
        // as a fetch failure (that's the case just handled above via Gaps).
        var anyCandidates = targetElections.Count == 0;
        foreach (var election in targetElections)
        {
            Console.WriteLine($"  {election.Name} ({election.ElectionDate:yyyy-MM-dd})...");
            var rawCandidates = await candidateClient.FetchCandidatesAsync(_year, int.Parse(election.ElectionId));

            if (rawCandidates.Count == 0)
            {
                result.Gaps.Add(
                    $"{election.Name} ({election.ElectionDate:yyyy-MM-dd}): no candidates returned by findQualifiedCandidates. " +
                    "Filing may not be complete yet; re-run closer to the election.");
                result.PendingElections.Add(election);
                continue;
            }

            var deduped = Deduplicator.RemoveDuplicates(rawCandidates, TxCandidateMapper.DeduplicationKey);
            foreach (var candidate in deduped)
                result.Candidates.Add(TxCandidateMapper.ToCandidateData(candidate, election, _config.CandidatesUrl));

            anyCandidates = true;
        }

        if (!anyCandidates)
            throw new InvalidOperationException(
                "Every upcoming election returned zero candidates; refusing to write hollow outputs. " +
                "Check https://goelect.txelections.civixapps.com manually.");

        result.Candidates.Sort(CompareCandidates);
        BuildSourcesManifest(result);
        return result;
    }

    private static int CompareCandidates(CandidateData a, CandidateData b)
    {
        var c = string.CompareOrdinal(a.ElectionDate, b.ElectionDate);
        if (c != 0) return c;
        c = string.CompareOrdinal(a.Office, b.Office);
        return c != 0 ? c : string.CompareOrdinal(a.CandidateName, b.CandidateName);
    }

    private void BuildSourcesManifest(CollectResult result)
    {
        result.SourceGroups["elections"] = new[]
        {
            new { url = _config.ElectionsUrl(_year), format = "json" },
        };
        result.SourceGroups["statewide_candidates"] = new[]
        {
            new { url = _config.CandidatesUrl, format = "json (POST)" },
        };
        result.SourceGroups["verification_only"] = new[]
        {
            new { url = "https://ballotpedia.org/Texas_elections," + _year, format = "html" },
        };

        if (result.PendingElections.Count > 0)
        {
            result.NextRun = new
            {
                recommended_after = DateTime.UtcNow.Date.AddDays(7).ToString("yyyy-MM-dd"),
                reason = $"{result.PendingElections.Count} election(s) had no candidates yet as of this run; " +
                         "candidate filing periods are typically still open. Re-run in about a week.",
            };
        }
        else if (result.Elections.Count == 0)
        {
            result.NextRun = new
            {
                recommended_after = $"{_year + 1}-01-01",
                reason = $"All elections listed for {_year} have already passed; nothing was collected this run. " +
                         $"Re-run once {_year + 1} elections are listed.",
            };
        }
        else
        {
            result.NextRun = new
            {
                recommended_after = $"{_year + 1}-01-01",
                reason = $"All {_year} elections collected. Re-run once {_year + 1} elections are listed.",
            };
        }
    }
}
