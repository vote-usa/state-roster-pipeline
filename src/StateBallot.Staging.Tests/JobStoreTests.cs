using Dapper;
using Testcontainers.MySql;

namespace StateBallot.Staging.Tests;

/// <summary>The console's job queue against a real MySQL. Needs Docker.</summary>
[Trait("Category", "Integration")]
public sealed class JobStoreIntegrationTests : IAsyncLifetime
{
    private readonly MySqlContainer _mysql = new MySqlBuilder("mysql:8.4")
        .WithDatabase("roster_staging")
        .WithUsername("roster")
        .WithPassword("roster")
        .Build();

    private StagingDb _db = null!;
    private JobStore _jobs = null!;

    public async Task InitializeAsync()
    {
        await _mysql.StartAsync();
        _db = new StagingDb(_mysql.GetConnectionString(), _mysql.GetConnectionString());
        await new Migrator(_db.StagingConnectionString).ApplyAsync();
        _jobs = new JobStore(_db);
    }

    public Task DisposeAsync() => _mysql.DisposeAsync().AsTask();

    private static JobStore.NewJob Job(string state, string kind = JobStore.Both, int? capture = null) =>
        new(state, 2026, kind, capture, null, "tester");

    [Fact]
    public async Task Jobs_AreClaimedOldestFirst_AndOnlyOnce()
    {
        var first = await _jobs.EnqueueAsync(Job("WV"));
        var second = await _jobs.EnqueueAsync(Job("MD") with { ElectionFilter = "2026-11-03" });
        Assert.Equal("queued", (await _jobs.ActiveForStateAsync("WV"))!.Status);

        var claimed = await _jobs.ClaimNextAsync();
        Assert.Equal(first, claimed!.JobId);
        Assert.Equal("running", claimed.Status);
        Assert.NotNull(claimed.StartedAt);

        var next = await _jobs.ClaimNextAsync();
        Assert.Equal(second, next!.JobId);
        Assert.Equal("2026-11-03", next.ElectionFilter);
        Assert.Null(await _jobs.ClaimNextAsync());

        await _jobs.SucceedAsync(first, captureId: 7, passId: 9);
        var done = await _jobs.GetAsync(first);
        Assert.Equal(("succeeded", 7, 9), (done!.Status, done.CaptureId, done.PassId));
        Assert.Null(await _jobs.ActiveForStateAsync("WV"));
        Assert.Equal(new[] { second, first }, (await _jobs.RecentAsync(10)).Select(j => j.JobId).ToArray());
    }

    [Fact]
    public async Task FailedJob_PointsAtTheCaptureAndPassItBegan()
    {
        await using var cn = await _db.OpenStagingAsync();
        // An older web capture for the same state must not be picked up.
        await cn.ExecuteAsync(
            "INSERT INTO Captures (CaptureId, StateCode, Year, Status, Source, StartedAt) VALUES (1, 'CA', 2026, 'succeeded', 'web', '2020-01-01 00:00:00')");

        await _jobs.EnqueueAsync(Job("CA"));
        var captureJob = (await _jobs.ClaimNextAsync())!;
        await cn.ExecuteAsync(
            "INSERT INTO Captures (CaptureId, StateCode, Year, Status, Source, StartedAt) VALUES (2, 'CA', 2026, 'failed', 'web', UTC_TIMESTAMP())");
        await _jobs.FailAsync(captureJob, "503 from the source");

        var failedCapture = await _jobs.GetAsync(captureJob.JobId);
        Assert.Equal(("failed", 2, null, "503 from the source"),
            (failedCapture!.Status, failedCapture.CaptureId, failedCapture.PassId, failedCapture.ErrorText));

        await _jobs.EnqueueAsync(Job("CA", JobStore.Normalize, capture: 1));
        var normalizeJob = (await _jobs.ClaimNextAsync())!;
        await cn.ExecuteAsync(
            "INSERT INTO CollectionPasses (PassId, CaptureId, StateCode, Year, Status, Source, RequestedAt) VALUES (5, 1, 'CA', 2026, 'failed', 'web', UTC_TIMESTAMP())");
        await _jobs.FailAsync(normalizeJob, "bad date");

        var failedNormalize = await _jobs.GetAsync(normalizeJob.JobId);
        Assert.Equal((1, 5), (failedNormalize!.CaptureId, failedNormalize.PassId));
    }

    [Fact]
    public async Task InterruptedWork_IsFailedAtStartup_ButCliRowsAreLeftAlone()
    {
        await using var cn = await _db.OpenStagingAsync();
        await cn.ExecuteAsync(
            """
            INSERT INTO Captures (CaptureId, StateCode, Year, Status, Source, StartedAt) VALUES
                (1, 'TX', 2026, 'running', 'web', UTC_TIMESTAMP()),
                (2, 'TX', 2026, 'running', 'cli', UTC_TIMESTAMP());
            """);
        await _jobs.EnqueueAsync(Job("TX"));
        var queued = await _jobs.EnqueueAsync(Job("WA"));
        var running = (await _jobs.ClaimNextAsync())!;

        Assert.Equal(1, await _jobs.FailInterruptedAsync());

        Assert.Equal("failed", (await _jobs.GetAsync(running.JobId))!.Status);
        Assert.Equal("queued", (await _jobs.GetAsync(queued))!.Status);
        var captures = (await cn.QueryAsync<(int CaptureId, string Status)>("SELECT CaptureId, Status FROM Captures ORDER BY CaptureId")).ToArray();
        Assert.Equal(new[] { (1, "failed"), (2, "running") }, captures);
    }
}
