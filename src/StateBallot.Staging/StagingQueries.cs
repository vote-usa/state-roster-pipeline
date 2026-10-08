using System.Text.Json;
using System.Text.Json.Serialization;
using Dapper;
using StateBallot.Core.Raw;

namespace StateBallot.Staging;

/// <summary>
/// Read side of roster_staging: captures, passes, runs and their rows, shaped for display.
/// Nothing here writes. Timestamps come back as UTC ISO strings and dates as yyyy-MM-dd.
/// </summary>
public sealed class StagingQueries
{
    private const string CaptureColumns =
        """
        CaptureId, StateCode, Year, Status, Source, RequestedBy,
        DATE_FORMAT(StartedAt, '%Y-%m-%dT%H:%i:%sZ') AS StartedAt,
        DATE_FORMAT(FinishedAt, '%Y-%m-%dT%H:%i:%sZ') AS FinishedAt,
        Wayback, RawDir, FetchCount, RawBytes, GitSha, ErrorText
        """;

    private const string PassColumns =
        """
        PassId, CaptureId, StateCode, Year, Status, Source, RequestedBy,
        DATE_FORMAT(RequestedAt, '%Y-%m-%dT%H:%i:%sZ') AS RequestedAt,
        DATE_FORMAT(FinishedAt, '%Y-%m-%dT%H:%i:%sZ') AS FinishedAt,
        DATE_FORMAT(ElectionFilter, '%Y-%m-%d') AS ElectionFilter, GitSha,
        RunCount, CandidateCount, MeasureCount, CountyBallotCount, CountyDirectoryCount,
        ProposedMeasureCount, UnassignedRowCount, GapsJson, ErrorText
        """;

    private const string RunColumns =
        """
        r.RunId, r.PassId, r.StateCode, DATE_FORMAT(r.ElectionDate, '%Y-%m-%d') AS ElectionDate,
        r.ElectionType, r.ElectionName, r.SourceElectionId, r.IsPending,
        r.CandidateCount, r.MeasureCount, r.CountyBallotCount,
        DATE_FORMAT(r.CreatedAt, '%Y-%m-%dT%H:%i:%sZ') AS CreatedAt
        """;

    private readonly StagingDb _db;
    private readonly string _dataRoot;

    /// <param name="dataRoot">Pipeline data root, used to tell whether a capture's payloads are still on disk.</param>
    public StagingQueries(StagingDb db, string dataRoot)
    {
        _db = db;
        _dataRoot = dataRoot;
    }

    public sealed class CaptureView
    {
        public int CaptureId { get; init; }
        public string StateCode { get; init; } = "";
        public int Year { get; init; }
        public string Status { get; init; } = "";
        public string Source { get; init; } = "";
        public string RequestedBy { get; init; } = "";
        public string StartedAt { get; init; } = "";
        public string? FinishedAt { get; init; }
        public string? Wayback { get; init; }
        public string? RawDir { get; init; }
        public int FetchCount { get; init; }
        public long RawBytes { get; init; }
        public string? GitSha { get; init; }
        public string? ErrorText { get; init; }
        public string? LogText { get; init; }

        /// <summary>False once --keep-raw has pruned the payloads, after which the capture cannot be normalized.</summary>
        public bool FilesPresent { get; set; }
    }

    public sealed class FetchView
    {
        public int Seq { get; init; }
        public string? Role { get; init; }
        [JsonIgnore] public string? KeysJson { get; init; }
        public JsonElement? Keys => Parse(KeysJson);
        public string Method { get; init; } = "";
        public string Url { get; init; } = "";
        public int? Status { get; init; }
        public string? ContentType { get; init; }
        public long? Bytes { get; init; }
        public long DurationMs { get; init; }
        public string? Error { get; init; }
    }

    public sealed record SourceView(string Group, string Url, string Format, bool Hashed);

    public sealed record NextRunView(string? After, string? Reason);

    public sealed class PassView
    {
        public int PassId { get; init; }
        public int? CaptureId { get; init; }
        public string StateCode { get; init; } = "";
        public int Year { get; init; }
        public string Status { get; init; } = "";
        public string Source { get; init; } = "";
        public string RequestedBy { get; init; } = "";
        public string RequestedAt { get; init; } = "";
        public string? FinishedAt { get; init; }
        public string? ElectionFilter { get; init; }
        public string? GitSha { get; init; }
        public int RunCount { get; init; }
        public int CandidateCount { get; init; }
        public int MeasureCount { get; init; }
        public int CountyBallotCount { get; init; }
        public int CountyDirectoryCount { get; init; }
        public int ProposedMeasureCount { get; init; }
        public int UnassignedRowCount { get; init; }
        [JsonIgnore] public string? GapsJson { get; init; }
        [JsonIgnore] public string? SourcesJson { get; init; }
        public string? Summary { get; init; }
        public string? LogText { get; init; }
        public string? ErrorText { get; init; }

        public IReadOnlyList<string> Gaps =>
            Parse(GapsJson) is { ValueKind: JsonValueKind.Array } gaps
                ? gaps.EnumerateArray().Select(g => g.ToString()).ToList()
                : [];

        /// <summary>When the collector recommends collecting again. Only filled where SourcesJson was selected.</summary>
        public NextRunView? NextRun =>
            Parse(SourcesJson) is { ValueKind: JsonValueKind.Object } sources
            && sources.TryGetProperty("next_run", out var next) && next.ValueKind == JsonValueKind.Object
                ? new NextRunView(Text(next, "recommended_after"), Text(next, "reason"))
                : null;

        /// <summary>The source URLs the pass reported, one row per URL. County ballot sources are summarized as one row.</summary>
        public IReadOnlyList<SourceView> Sources
        {
            get
            {
                var rows = new List<SourceView>();
                if (Parse(SourcesJson) is not { ValueKind: JsonValueKind.Object } sources)
                    return rows;
                foreach (var group in sources.EnumerateObject())
                {
                    if (group.Name is "gaps" or "next_run")
                        continue;
                    if (group.Value.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var entry in group.Value.EnumerateArray())
                            rows.Add(new SourceView(group.Name, Text(entry, "url") ?? "", Text(entry, "format") ?? "", HasHash(entry)));
                    }
                    else if (group.Value.ValueKind == JsonValueKind.Object)
                    {
                        var counties = group.Value.EnumerateObject().ToList();
                        var urls = counties.Sum(c => c.Value.ValueKind == JsonValueKind.Array ? c.Value.GetArrayLength() : 0);
                        rows.Add(new SourceView(group.Name, $"{counties.Count} counties, {urls} source URLs", "", true));
                    }
                }
                return rows;
            }
        }

        private static bool HasHash(JsonElement entry) =>
            entry.ValueKind == JsonValueKind.Object
            && entry.TryGetProperty("payload_sha256", out var hashes)
            && hashes.ValueKind == JsonValueKind.Array && hashes.GetArrayLength() > 0;
    }

    public sealed class RunView
    {
        public int RunId { get; init; }
        public int PassId { get; init; }
        public string StateCode { get; init; } = "";
        public string ElectionDate { get; init; } = "";
        public string ElectionType { get; init; } = "";
        public string ElectionName { get; init; } = "";
        public string SourceElectionId { get; init; } = "";
        public bool IsPending { get; init; }
        public int CandidateCount { get; init; }
        public int MeasureCount { get; init; }
        public int CountyBallotCount { get; init; }
        public string CreatedAt { get; init; } = "";

        /// <summary>Runs stored for the same election. Only filled by <see cref="LatestRunsAsync"/>.</summary>
        public long RunCount { get; init; }
    }

    public sealed class CandidateView
    {
        public string Office { get; init; } = "";
        public string? District { get; init; }
        public string? County { get; init; }
        public string Name { get; init; } = "";
        public string? Party { get; init; }
        public string? Status { get; init; }
        public string? OcdDivisionId { get; init; }
        public string? SourceOfficeType { get; init; }
    }

    public sealed class MeasureView
    {
        public string MeasureId { get; init; } = "";
        public string? Title { get; init; }
        public string Jurisdiction { get; init; } = "";
        public string? County { get; init; }
    }

    public sealed class CountyBallotView
    {
        public string County { get; init; } = "";
        public int CandidateCount { get; init; }
        public int MeasureCount { get; init; }
    }

    public sealed record Page<T>(long Total, IReadOnlyList<T> Rows);

    /// <summary>The newest capture of each state.</summary>
    public Task<IReadOnlyList<CaptureView>> LatestCapturesAsync(CancellationToken ct = default) =>
        CapturesWhereAsync("CaptureId IN (SELECT MAX(CaptureId) FROM Captures GROUP BY StateCode)", null, ct);

    public Task<IReadOnlyList<CaptureView>> CapturesAsync(string stateCode, CancellationToken ct = default) =>
        CapturesWhereAsync("StateCode = @StateCode ORDER BY CaptureId DESC", new { StateCode = stateCode }, ct);

    public Task<IReadOnlyList<CaptureView>> RecentCapturesAsync(int limit, CancellationToken ct = default) =>
        CapturesWhereAsync("1 = 1 ORDER BY CaptureId DESC LIMIT @Limit", new { Limit = limit }, ct);

    public async Task<CaptureView?> CaptureAsync(int captureId, CancellationToken ct = default)
    {
        await using var cn = await _db.OpenStagingAsync(ct);
        var capture = await cn.QuerySingleOrDefaultAsync<CaptureView>(new CommandDefinition(
            $"SELECT {CaptureColumns}, LogText FROM Captures WHERE CaptureId = @CaptureId",
            new { CaptureId = captureId }, cancellationToken: ct));
        if (capture is not null)
            capture.FilesPresent = FilesPresent(capture.RawDir);
        return capture;
    }

    public async Task<IReadOnlyList<FetchView>> FetchesAsync(int captureId, CancellationToken ct = default)
    {
        await using var cn = await _db.OpenStagingAsync(ct);
        return (await cn.QueryAsync<FetchView>(new CommandDefinition(
            """
            SELECT Seq, Role, KeysJson, Method, Url, Status, ContentType, Bytes, DurationMs, Error
            FROM CaptureFetches WHERE CaptureId = @CaptureId ORDER BY Seq
            """,
            new { CaptureId = captureId }, cancellationToken: ct))).AsList();
    }

    /// <summary>The newest pass of each state, any status.</summary>
    public Task<IReadOnlyList<PassView>> LatestPassesAsync(CancellationToken ct = default) =>
        PassesWhereAsync(PassColumns, "PassId IN (SELECT MAX(PassId) FROM CollectionPasses GROUP BY StateCode)", null, ct);

    /// <summary>The newest succeeded pass of each state, with its sources so gaps and the next-run advice are available.</summary>
    public Task<IReadOnlyList<PassView>> LatestSucceededPassesAsync(CancellationToken ct = default) =>
        PassesWhereAsync(
            $"{PassColumns}, SourcesJson",
            "PassId IN (SELECT MAX(PassId) FROM CollectionPasses WHERE Status = 'succeeded' GROUP BY StateCode)", null, ct);

    public Task<IReadOnlyList<PassView>> PassesAsync(string stateCode, CancellationToken ct = default) =>
        PassesWhereAsync(PassColumns, "StateCode = @StateCode ORDER BY PassId DESC", new { StateCode = stateCode }, ct);

    public Task<IReadOnlyList<PassView>> PassesForCaptureAsync(int captureId, CancellationToken ct = default) =>
        PassesWhereAsync(PassColumns, "CaptureId = @CaptureId ORDER BY PassId DESC", new { CaptureId = captureId }, ct);

    public Task<IReadOnlyList<PassView>> RecentPassesAsync(int limit, CancellationToken ct = default) =>
        PassesWhereAsync(PassColumns, "1 = 1 ORDER BY PassId DESC LIMIT @Limit", new { Limit = limit }, ct);

    public async Task<PassView?> PassAsync(int passId, CancellationToken ct = default) =>
        (await PassesWhereAsync(
            $"{PassColumns}, SourcesJson, Summary, LogText", "PassId = @PassId", new { PassId = passId }, ct)).SingleOrDefault();

    /// <summary>
    /// The newest run of every election, with how many runs that election has. An election is a
    /// state, date, type and source election id, because two elections can share a date and a type.
    /// Elections on one date keep the order they were first stored in, however often they are re-run.
    /// </summary>
    public async Task<IReadOnlyList<RunView>> LatestRunsAsync(CancellationToken ct = default)
    {
        await using var cn = await _db.OpenStagingAsync(ct);
        return (await cn.QueryAsync<RunView>(new CommandDefinition(
            $"""
            SELECT * FROM (
                SELECT {RunColumns},
                    ROW_NUMBER() OVER (PARTITION BY r.StateCode, r.ElectionDate, r.ElectionType, r.SourceElectionId ORDER BY r.RunId DESC) AS Newest,
                    COUNT(*) OVER (PARTITION BY r.StateCode, r.ElectionDate, r.ElectionType, r.SourceElectionId) AS RunCount,
                    MIN(r.RunId) OVER (PARTITION BY r.StateCode, r.ElectionDate, r.ElectionType, r.SourceElectionId) AS FirstRunId
                FROM Runs r) ranked
            WHERE Newest = 1 ORDER BY StateCode, ElectionDate, FirstRunId
            """,
            cancellationToken: ct))).AsList();
    }

    public Task<IReadOnlyList<RunView>> RunsAsync(string stateCode, CancellationToken ct = default) =>
        RunsWhereAsync("r.StateCode = @StateCode", new { StateCode = stateCode }, ct);

    public Task<IReadOnlyList<RunView>> RunsForCaptureAsync(int captureId, CancellationToken ct = default) =>
        RunsWhereAsync(
            "r.PassId IN (SELECT PassId FROM CollectionPasses WHERE CaptureId = @CaptureId)", new { CaptureId = captureId }, ct);

    public Task<IReadOnlyList<RunView>> RunsForPassesAsync(IEnumerable<int> passIds, CancellationToken ct = default) =>
        RunsWhereAsync("r.PassId IN @PassIds", new { PassIds = passIds.DefaultIfEmpty(-1).ToArray() }, ct);

    public async Task<RunView?> RunAsync(int runId, CancellationToken ct = default) =>
        (await RunsWhereAsync("r.RunId = @RunId", new { RunId = runId }, ct)).SingleOrDefault();

    /// <summary>One page of a run's candidates. A search matches any of office, district, county, name, party or filing status.</summary>
    public async Task<Page<CandidateView>> CandidatesAsync(
        int runId, string? search, int offset, int limit, CancellationToken ct = default)
    {
        var where = "RunId = @RunId";
        if (!string.IsNullOrWhiteSpace(search))
            where += " AND CONCAT_WS(' ', Office, District, County, CandidateName, Party, Status) LIKE @Search";
        var args = new { RunId = runId, Search = $"%{EscapeLike(search)}%", Offset = offset, Limit = limit };

        await using var cn = await _db.OpenStagingAsync(ct);
        var total = await cn.ExecuteScalarAsync<long>(new CommandDefinition(
            $"SELECT COUNT(*) FROM RunCandidates WHERE {where}", args, cancellationToken: ct));
        var rows = await cn.QueryAsync<CandidateView>(new CommandDefinition(
            $"""
            SELECT Office, District, County, CandidateName AS Name, Party, Status, OcdDivisionId, SourceOfficeType
            FROM RunCandidates WHERE {where} ORDER BY RunCandidateId LIMIT @Limit OFFSET @Offset
            """,
            args, cancellationToken: ct));
        return new Page<CandidateView>(total, rows.AsList());
    }

    public async Task<Page<MeasureView>> MeasuresAsync(int runId, int offset, int limit, CancellationToken ct = default)
    {
        var args = new { RunId = runId, Offset = offset, Limit = limit };
        await using var cn = await _db.OpenStagingAsync(ct);
        var total = await cn.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT COUNT(*) FROM RunMeasures WHERE RunId = @RunId", args, cancellationToken: ct));
        var rows = await cn.QueryAsync<MeasureView>(new CommandDefinition(
            """
            SELECT MeasureId, Title, Jurisdiction, County
            FROM RunMeasures WHERE RunId = @RunId ORDER BY RunMeasureId LIMIT @Limit OFFSET @Offset
            """,
            args, cancellationToken: ct));
        return new Page<MeasureView>(total, rows.AsList());
    }

    public async Task<IReadOnlyList<CountyBallotView>> CountyBallotsAsync(int runId, CancellationToken ct = default)
    {
        await using var cn = await _db.OpenStagingAsync(ct);
        return (await cn.QueryAsync<CountyBallotView>(new CommandDefinition(
            "SELECT County, CandidateCount, MeasureCount FROM RunCountyBallots WHERE RunId = @RunId ORDER BY County",
            new { RunId = runId }, cancellationToken: ct))).AsList();
    }

    private async Task<IReadOnlyList<CaptureView>> CapturesWhereAsync(string where, object? args, CancellationToken ct)
    {
        await using var cn = await _db.OpenStagingAsync(ct);
        var captures = (await cn.QueryAsync<CaptureView>(new CommandDefinition(
            $"SELECT {CaptureColumns} FROM Captures WHERE {where}", args, cancellationToken: ct))).AsList();
        foreach (var capture in captures)
            capture.FilesPresent = FilesPresent(capture.RawDir);
        return captures;
    }

    private async Task<IReadOnlyList<PassView>> PassesWhereAsync(string columns, string where, object? args, CancellationToken ct)
    {
        await using var cn = await _db.OpenStagingAsync(ct);
        return (await cn.QueryAsync<PassView>(new CommandDefinition(
            $"SELECT {columns} FROM CollectionPasses WHERE {where}", args, cancellationToken: ct))).AsList();
    }

    private async Task<IReadOnlyList<RunView>> RunsWhereAsync(string where, object? args, CancellationToken ct)
    {
        await using var cn = await _db.OpenStagingAsync(ct);
        return (await cn.QueryAsync<RunView>(new CommandDefinition(
            $"SELECT {RunColumns} FROM Runs r WHERE {where} ORDER BY r.RunId DESC", args, cancellationToken: ct))).AsList();
    }

    // The same test CollectorRunner makes before it will normalize a capture.
    private bool FilesPresent(string? rawDir) =>
        rawDir is not null && File.Exists(Path.Combine(_dataRoot, rawDir, RawSink.LogFileName));

    private static string EscapeLike(string? text) =>
        (text ?? "").Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    private static JsonElement? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    private static string? Text(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
