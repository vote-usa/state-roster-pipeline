using Dapper;

namespace StateBallot.Staging;

/// <summary>
/// The console's job queue in roster_staging. The API enqueues, one worker claims the oldest
/// queued job and records how it ended. Timestamps are read back as UTC ISO strings.
/// </summary>
public sealed class JobStore
{
    public const string Capture = "capture";
    public const string Normalize = "normalize";
    public const string Both = "both";

    private const string Columns =
        """
        JobId, StateCode, Year, Kind, NormalizeCaptureId, DATE_FORMAT(ElectionFilter, '%Y-%m-%d') AS ElectionFilter,
        Status, RequestedBy,
        DATE_FORMAT(RequestedAt, '%Y-%m-%dT%H:%i:%sZ') AS RequestedAt,
        DATE_FORMAT(StartedAt, '%Y-%m-%dT%H:%i:%sZ') AS StartedAt,
        DATE_FORMAT(FinishedAt, '%Y-%m-%dT%H:%i:%sZ') AS FinishedAt,
        CaptureId, PassId, ErrorText
        """;

    private readonly StagingDb _db;

    public JobStore(StagingDb db) => _db = db;

    public sealed record NewJob(
        string StateCode, int Year, string Kind, int? NormalizeCaptureId, string? ElectionFilter, string RequestedBy);

    public sealed class JobView
    {
        public int JobId { get; init; }
        public string StateCode { get; init; } = "";
        public int Year { get; init; }
        public string Kind { get; init; } = "";
        public int? NormalizeCaptureId { get; init; }
        public string? ElectionFilter { get; init; }
        public string Status { get; init; } = "";
        public string RequestedBy { get; init; } = "";
        public string RequestedAt { get; init; } = "";
        public string? StartedAt { get; init; }
        public string? FinishedAt { get; init; }
        public int? CaptureId { get; init; }
        public int? PassId { get; init; }
        public string? ErrorText { get; init; }
    }

    public async Task<int> EnqueueAsync(NewJob job, CancellationToken ct = default)
    {
        await using var cn = await _db.OpenStagingAsync(ct);
        return await cn.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            INSERT INTO Jobs (StateCode, Year, Kind, NormalizeCaptureId, ElectionFilter, Status, RequestedBy, RequestedAt)
            VALUES (@StateCode, @Year, @Kind, @NormalizeCaptureId, @ElectionFilter, 'queued', @RequestedBy, UTC_TIMESTAMP());
            SELECT LAST_INSERT_ID();
            """,
            job, cancellationToken: ct));
    }

    /// <summary>The state's queued or running job, if it has one.</summary>
    public async Task<JobView?> ActiveForStateAsync(string stateCode, CancellationToken ct = default)
    {
        await using var cn = await _db.OpenStagingAsync(ct);
        return await cn.QueryFirstOrDefaultAsync<JobView>(new CommandDefinition(
            $"SELECT {Columns} FROM Jobs WHERE StateCode = @StateCode AND Status IN ('queued', 'running') ORDER BY JobId",
            new { StateCode = stateCode }, cancellationToken: ct));
    }

    /// <summary>Marks the oldest queued job running and returns it, or null when nothing is queued.</summary>
    public async Task<JobView?> ClaimNextAsync(CancellationToken ct = default)
    {
        await using var cn = await _db.OpenStagingAsync(ct);
        await using var tx = await cn.BeginTransactionAsync(ct);
        var jobId = await cn.ExecuteScalarAsync<int?>(new CommandDefinition(
            "SELECT JobId FROM Jobs WHERE Status = 'queued' ORDER BY JobId LIMIT 1 FOR UPDATE",
            transaction: tx, cancellationToken: ct));
        if (jobId is null)
            return null;

        await cn.ExecuteAsync(new CommandDefinition(
            "UPDATE Jobs SET Status = 'running', StartedAt = UTC_TIMESTAMP() WHERE JobId = @JobId",
            new { JobId = jobId }, transaction: tx, cancellationToken: ct));
        var job = await cn.QuerySingleAsync<JobView>(new CommandDefinition(
            $"SELECT {Columns} FROM Jobs WHERE JobId = @JobId", new { JobId = jobId }, transaction: tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
        return job;
    }

    public async Task SucceedAsync(int jobId, int? captureId, int? passId, CancellationToken ct = default)
    {
        await using var cn = await _db.OpenStagingAsync(ct);
        await cn.ExecuteAsync(new CommandDefinition(
            """
            UPDATE Jobs SET Status = 'succeeded', FinishedAt = UTC_TIMESTAMP(), CaptureId = @CaptureId, PassId = @PassId
            WHERE JobId = @JobId
            """,
            new { JobId = jobId, CaptureId = captureId, PassId = passId }, cancellationToken: ct));
    }

    /// <summary>
    /// Marks the job failed. A failed run throws before it can say which capture or pass it
    /// began, so they are found as the web-sourced rows for the state begun since the job
    /// started. That is exact while one worker runs one job at a time.
    /// </summary>
    public async Task FailAsync(JobView job, string error, CancellationToken ct = default)
    {
        await using var cn = await _db.OpenStagingAsync(ct);
        await cn.ExecuteAsync(new CommandDefinition(
            """
            UPDATE Jobs j SET j.Status = 'failed', j.FinishedAt = UTC_TIMESTAMP(), j.ErrorText = @Error,
                j.CaptureId = COALESCE(j.NormalizeCaptureId,
                    (SELECT MAX(c.CaptureId) FROM Captures c
                     WHERE c.StateCode = j.StateCode AND c.Source = 'web' AND c.StartedAt >= j.StartedAt)),
                j.PassId =
                    (SELECT MAX(p.PassId) FROM CollectionPasses p
                     WHERE p.StateCode = j.StateCode AND p.Source = 'web' AND p.RequestedAt >= j.StartedAt)
            WHERE j.JobId = @JobId
            """,
            new { job.JobId, Error = error }, cancellationToken: ct));
    }

    /// <summary>
    /// Fails whatever a stopped worker left running: its job, and the capture or pass row that
    /// job had begun. Call once at startup, before claiming. Returns the number of jobs failed.
    /// </summary>
    public async Task<int> FailInterruptedAsync(CancellationToken ct = default)
    {
        const string reason = "The console stopped while this was running.";
        await using var cn = await _db.OpenStagingAsync(ct);
        await cn.ExecuteAsync(new CommandDefinition(
            """
            UPDATE Captures SET Status = 'failed', FinishedAt = UTC_TIMESTAMP(), ErrorText = @Reason
            WHERE Status = 'running' AND Source = 'web';
            UPDATE CollectionPasses SET Status = 'failed', FinishedAt = UTC_TIMESTAMP(), ErrorText = @Reason
            WHERE Status = 'running' AND Source = 'web';
            """,
            new { Reason = reason }, cancellationToken: ct));
        return await cn.ExecuteAsync(new CommandDefinition(
            "UPDATE Jobs SET Status = 'failed', FinishedAt = UTC_TIMESTAMP(), ErrorText = @Reason WHERE Status = 'running'",
            new { Reason = reason }, cancellationToken: ct));
    }

    public async Task<JobView?> GetAsync(int jobId, CancellationToken ct = default)
    {
        await using var cn = await _db.OpenStagingAsync(ct);
        return await cn.QuerySingleOrDefaultAsync<JobView>(new CommandDefinition(
            $"SELECT {Columns} FROM Jobs WHERE JobId = @JobId", new { JobId = jobId }, cancellationToken: ct));
    }

    public async Task<IReadOnlyList<JobView>> RecentAsync(int limit, CancellationToken ct = default)
    {
        await using var cn = await _db.OpenStagingAsync(ct);
        return (await cn.QueryAsync<JobView>(new CommandDefinition(
            $"SELECT {Columns} FROM Jobs ORDER BY JobId DESC LIMIT @Limit", new { Limit = limit }, cancellationToken: ct))).AsList();
    }
}
