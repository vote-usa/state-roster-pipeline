using System.Globalization;
using System.Text.Json;
using StateBallot.Core.Output;

namespace StateBallot.Core.Publishing;

/// <summary>
/// Writes a collector result as the election-scoped tree:
///
///   &lt;stateDir&gt;/county_directory.json
///   &lt;stateDir&gt;/proposed_measures.json          statewide measures with no election date yet
///   &lt;stateDir&gt;/&lt;yyyy-MM-dd&gt;/elections.json      every election on that date
///   &lt;stateDir&gt;/&lt;yyyy-MM-dd&gt;/candidates.json
///   &lt;stateDir&gt;/&lt;yyyy-MM-dd&gt;/measures.json
///   &lt;stateDir&gt;/&lt;yyyy-MM-dd&gt;/county_ballots.json  only when the source provides per-county ballots
///   &lt;stateDir&gt;/&lt;yyyy-MM-dd&gt;/run.json
///
/// Only the date directories present in this run are rewritten. Dates from earlier runs
/// (other years, back-fills) are left alone. election_type is normalized to the published
/// vocabulary here and the raw value is recorded in run.json.
/// </summary>
public sealed class ElectionTreeWriter
{
    public const string ElectionsFile = "elections.json";
    public const string CandidatesFile = "candidates.json";
    public const string MeasuresFile = "measures.json";
    public const string CountyBallotsFile = "county_ballots.json";
    public const string CountyDirectoryFile = "county_directory.json";
    public const string ProposedMeasuresFile = "proposed_measures.json";
    public const string RunFile = "run.json";

    private static readonly string[] LegacyStateLevelFiles =
    [
        "elections.json", "elections.csv", "candidates.json", "candidates.csv",
        "measures.json", "measures.csv", "county_ballots.json", "county_ballots.csv",
    ];

    private readonly string _stateDir;
    private readonly SchemaValidator _validator;

    public ElectionTreeWriter(string stateDir, SchemaValidator? validator = null)
    {
        _stateDir = stateDir;
        _validator = validator ?? new SchemaValidator();
    }

    public sealed record WriteReport(IReadOnlyList<string> ElectionDates, IReadOnlyList<string> FilesWritten);

    public WriteReport Write(CollectResult result, RunContext context)
    {
        Directory.CreateDirectory(_stateDir);
        RemoveLegacyFlatFiles();

        var written = new List<string>();
        var electionsByDate = result.Elections
            .Select(ResultWriter.ToElectionOut)
            .GroupBy(e => e.ElectionDate, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
        var candidatesByDate = result.Candidates
            .Select(ResultWriter.ToCandidateOut)
            .GroupBy(c => c.ElectionDate, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
        var measuresByDate = result.Measures.Concat(result.StatewideProposedMeasures)
            .Where(m => m.ElectionDate is not null)
            .Select(ResultWriter.ToMeasureOut)
            .GroupBy(m => m.ElectionDate!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
        var ballotsByDate = result.CountyBallots
            .Select(ResultWriter.ToBallotOut)
            .GroupBy(b => b.ElectionDate, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

        var dates = electionsByDate.Keys
            .Concat(candidatesByDate.Keys)
            .Concat(measuresByDate.Keys)
            .Concat(ballotsByDate.Keys)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

        var generatedAt = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        var pipelineCommit = PipelineInfo.GitCommit();
        var rawTypesByDate = RawElectionTypes(result);
        var pendingIds = result.PendingElections.Select(p => p.ElectionId).ToHashSet(StringComparer.Ordinal);

        foreach (var date in dates)
        {
            var dir = Path.Combine(_stateDir, date);
            if (Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
            Directory.CreateDirectory(dir);

            var elections = electionsByDate.GetValueOrDefault(date) ?? [];
            var candidates = candidatesByDate.GetValueOrDefault(date) ?? [];
            var measures = measuresByDate.GetValueOrDefault(date) ?? [];
            var ballots = ballotsByDate.GetValueOrDefault(date) ?? [];

            written.Add(WriteJson(dir, ElectionsFile, elections));
            written.Add(WriteJson(dir, CandidatesFile, candidates));
            written.Add(WriteJson(dir, MeasuresFile, measures));
            if (ballots.Count > 0)
                written.Add(WriteJson(dir, CountyBallotsFile, ballots));

            var run = new RunMetadata
            {
                State = StateOf(result),
                ElectionDate = date,
                Year = context.Year,
                GeneratedAt = generatedAt,
                PipelineCommit = pipelineCommit,
                PipelineVersion = PipelineInfo.Version,
                Wayback = context.Wayback,
                CliArgs = context.CliArgs,
                RequestedBy = context.RequestedBy,
                ElectionIds = elections.Select(e => e.ElectionId).ToList(),
                Pending = elections.Count > 0 && elections.All(e => pendingIds.Contains(e.ElectionId)),
                Counts = new RunCounts
                {
                    Elections = elections.Count,
                    Candidates = candidates.Count,
                    Measures = measures.Count,
                    CountyBallots = ballots.Count,
                },
                ElectionTypesRaw = rawTypesByDate.GetValueOrDefault(date) ?? new SortedDictionary<string, string>(StringComparer.Ordinal),
                Gaps = result.Gaps.ToList(),
                NextRun = result.Sources.NextRun is { } n
                    ? new NextRunOut
                    {
                        RecommendedAfter = n.RecommendedAfter,
                        Reason = n.Reason,
                        NextElectionDate = n.NextElectionDate,
                        NextElectionType = n.NextElectionType,
                    }
                    : null,
                Extra = new Dictionary<string, object?> { ["sources"] = SourcesObject(result) },
            };
            written.Add(WriteJson(dir, RunFile, run));
        }

        var directory = result.CountyDirectory.Select(ResultWriter.ToDirectoryOut).ToList();
        if (directory.Count > 0)
            written.Add(WriteJson(_stateDir, CountyDirectoryFile, directory));

        var proposed = result.StatewideProposedMeasures
            .Where(m => m.ElectionDate is null)
            .Select(ResultWriter.ToMeasureOut)
            .ToList();
        if (proposed.Count > 0)
            written.Add(WriteJson(_stateDir, ProposedMeasuresFile, proposed));

        var errors = _validator.ValidateDirectory(_stateDir);
        if (errors.Count > 0)
            throw new SchemaValidationException(errors);

        return new WriteReport(dates, written);
    }

    /// <summary>Flat per-state files from the previous layout do not belong beside the date directories.</summary>
    private void RemoveLegacyFlatFiles()
    {
        foreach (var name in LegacyStateLevelFiles)
        {
            var path = Path.Combine(_stateDir, name);
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    private static Dictionary<string, SortedDictionary<string, string>> RawElectionTypes(CollectResult result)
    {
        var map = new Dictionary<string, SortedDictionary<string, string>>(StringComparer.Ordinal);
        void Add(string date, string raw)
        {
            if (!map.TryGetValue(date, out var d))
                map[date] = d = new SortedDictionary<string, string>(StringComparer.Ordinal);
            d[raw] = ElectionTypes.Normalize(raw);
        }

        foreach (var e in result.Elections)
            Add(e.ElectionDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), e.ElectionType);
        foreach (var c in result.Candidates)
            Add(c.ElectionDate, c.ElectionType);
        foreach (var b in result.CountyBallots)
            Add(b.ElectionDate, b.ElectionType);
        return map;
    }

    private static Dictionary<string, object?> SourcesObject(CollectResult result)
    {
        var sources = result.Sources.ToJsonObject(result.Gaps);
        sources.Remove("gaps");
        sources.Remove("next_run");
        return sources;
    }

    private static string StateOf(CollectResult result) =>
        result.Elections.FirstOrDefault()?.State
        ?? result.Candidates.FirstOrDefault()?.State
        ?? result.CountyDirectory.FirstOrDefault()?.State
        ?? result.StatewideProposedMeasures.FirstOrDefault()?.State
        ?? "";

    private static string WriteJson<T>(string dir, string name, T rows)
    {
        var path = Path.Combine(dir, name);
        OutputWriter.WriteJson(path, rows);
        return path;
    }
}
