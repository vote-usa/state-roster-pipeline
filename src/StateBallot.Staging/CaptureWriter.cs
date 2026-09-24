using System.Text.Json;
using Dapper;
using StateBallot.Core.Raw;

namespace StateBallot.Staging;

/// <summary>
/// Records the capture stage in roster_staging: one Captures row per capture and one
/// CaptureFetches row per fetch. Failed captures keep their fetches, because those are
/// the evidence for what went wrong.
/// </summary>
public sealed class CaptureWriter
{
    private readonly StagingDb _db;

    public CaptureWriter(StagingDb db) => _db = db;

    public sealed record CaptureInfo(int CaptureId, string StateCode, int Year, string Status, string? RawDir);

    public async Task<int> BeginAsync(RunRequest request, string? gitSha, CancellationToken ct = default)
    {
        await using var cn = await _db.OpenStagingAsync(ct);
        return await cn.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            INSERT INTO Captures (StateCode, Year, Status, Source, RequestedBy, StartedAt, CliArgs, GitSha, Wayback)
            VALUES (@StateCode, @Year, 'running', @Source, @RequestedBy, UTC_TIMESTAMP(), @CliArgs, @GitSha, @Wayback);
            SELECT LAST_INSERT_ID();
            """,
            new
            {
                StateCode = request.State.ToUpperInvariant(),
                request.Year,
                request.Source,
                request.RequestedBy,
                request.CliArgs,
                GitSha = gitSha,
                request.Wayback,
            },
            cancellationToken: ct));
    }

    /// <summary>Marks the capture succeeded or failed and stores its fetch log.</summary>
    public async Task FinishAsync(
        int captureId, RawSink sink, string rawDir, string logText, string? error, CancellationToken ct = default)
    {
        await using var cn = await _db.OpenStagingAsync(ct);
        await using var tx = await cn.BeginTransactionAsync(ct);

        await cn.ExecuteAsync(new CommandDefinition(
            """
            UPDATE Captures SET Status = @Status, FinishedAt = UTC_TIMESTAMP(), RawDir = @RawDir,
                FetchCount = @FetchCount, RawBytes = @RawBytes, FetchLogSha256 = @FetchLogSha256,
                LogText = @LogText, ErrorText = @Error
            WHERE CaptureId = @CaptureId
            """,
            new
            {
                CaptureId = captureId,
                Status = error is null ? "succeeded" : "failed",
                RawDir = rawDir,
                FetchCount = sink.Entries.Count,
                RawBytes = sink.TotalBytes,
                FetchLogSha256 = sink.LogSha256(),
                LogText = logText,
                Error = error,
            },
            transaction: tx, cancellationToken: ct));

        if (sink.Entries.Count > 0)
        {
            await cn.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO CaptureFetches (CaptureId, Seq, FetchedAt, Role, KeysJson, Method, Url, RequestSha256,
                    Status, ContentType, Bytes, PayloadSha256, FileName, DurationMs, Error)
                VALUES (@CaptureId, @Seq, @FetchedAt, @Role, @KeysJson, @Method, @Url, @RequestSha256,
                    @Status, @ContentType, @Bytes, @PayloadSha256, @FileName, @DurationMs, @Error)
                """,
                sink.Entries.Select(f => new
                {
                    CaptureId = captureId, f.Seq, f.FetchedAt, f.Role,
                    KeysJson = f.Keys is null ? null : JsonSerializer.Serialize(f.Keys),
                    f.Method, f.Url, f.RequestSha256, f.Status, f.ContentType, f.Bytes, f.PayloadSha256,
                    f.FileName, f.DurationMs, f.Error,
                }).ToList(),
                transaction: tx, cancellationToken: ct));
        }

        await tx.CommitAsync(ct);
    }

    public async Task<CaptureInfo?> GetAsync(int captureId, CancellationToken ct = default)
    {
        await using var cn = await _db.OpenStagingAsync(ct);
        return await cn.QuerySingleOrDefaultAsync<CaptureInfo>(new CommandDefinition(
            "SELECT CaptureId, StateCode, Year, Status, RawDir FROM Captures WHERE CaptureId = @CaptureId",
            new { CaptureId = captureId },
            cancellationToken: ct));
    }
}
