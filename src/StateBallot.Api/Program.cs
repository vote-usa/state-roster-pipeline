using Microsoft.AspNetCore.Diagnostics;
using MySqlConnector;
using StateBallot.Api;
using StateBallot.Core;
using StateBallot.Staging;

// API for the run console in web/. Local only: no auth, so it listens on localhost unless told otherwise.
var builder = WebApplication.CreateBuilder(args);
if (string.IsNullOrEmpty(builder.Configuration["urls"]))
    builder.WebHost.UseUrls("http://localhost:5080");

// ROSTER_DATA_ROOT points the console at another data/ folder, e.g. a scratch copy next to a scratch database.
var db = StagingDb.FromEnvironment();
var dataRoot = Environment.GetEnvironmentVariable("ROSTER_DATA_ROOT") is { Length: > 0 } root
    ? Path.GetFullPath(root)
    : CollectorRunner.FindDataRoot();
var queries = new StagingQueries(db, dataRoot);
var jobs = new JobStore(db);

builder.Services.AddSingleton(db);
builder.Services.AddSingleton(jobs);
builder.Services.AddSingleton(new DataRoot(dataRoot));
builder.Services.AddHostedService<JobWorker>();

var app = builder.Build();
app.Logger.LogInformation("Staging {Staging}, data root {DataRoot}", StagingDb.Describe(db.StagingConnectionString), dataRoot);

app.UseExceptionHandler(handler => handler.Run(async context =>
{
    var error = context.Features.Get<IExceptionHandlerFeature>()?.Error;
    var unreachable = error is MySqlException;
    context.Response.StatusCode = unreachable ? StatusCodes.Status503ServiceUnavailable : StatusCodes.Status500InternalServerError;
    await context.Response.WriteAsJsonAsync(new
    {
        error = unreachable
            ? $"The staging database at {StagingDb.Describe(db.StagingConnectionString)} could not be queried: {error!.Message}"
            : error?.Message ?? "Unexpected error.",
    });
}));

var api = app.MapGroup("/api");

api.MapGet("/states", async (CancellationToken ct) =>
{
    var catalog = StateCatalog.LoadFromDataRoot(dataRoot);
    var captures = (await queries.LatestCapturesAsync(ct)).ToDictionary(c => c.StateCode);
    var passes = (await queries.LatestPassesAsync(ct)).ToDictionary(p => p.StateCode);
    var succeeded = (await queries.LatestSucceededPassesAsync(ct)).ToDictionary(p => p.StateCode);
    var elections = (await queries.LatestRunsAsync(ct)).ToLookup(r => r.StateCode);

    return catalog.Codes.Order().Select(code =>
    {
        var good = succeeded.GetValueOrDefault(code);
        return new
        {
            code,
            name = catalog.Get(code).Name,
            implemented = StateCatalog.IsImplemented(catalog.Get(code).Status),
            lastCapture = captures.GetValueOrDefault(code),
            lastPass = passes.GetValueOrDefault(code),
            gapCount = good?.Gaps.Count,
            nextRun = good?.NextRun,
            sources = good?.Sources ?? [],
            elections = elections[code],
        };
    });
});

api.MapGet("/states/{code}", async (string code, CancellationToken ct) =>
{
    code = code.ToUpperInvariant();
    var catalog = StateCatalog.LoadFromDataRoot(dataRoot);
    if (!catalog.TryGet(code, out var entry))
        return Results.NotFound(new { error = $"No state {code} in the catalog." });

    var good = await queries.LatestSucceededPassAsync(code, ct);
    return Results.Ok(new
    {
        code,
        name = entry.Name,
        implemented = StateCatalog.IsImplemented(entry.Status),
        gaps = good?.Gaps ?? [],
        nextRun = good?.NextRun,
        sources = good?.Sources ?? [],
        captures = await queries.CapturesAsync(code, ct),
        passes = await queries.PassesAsync(code, ct),
        runs = await queries.RunsAsync(code, ct),
    });
});

api.MapGet("/captures/{id:int}", async (int id, CancellationToken ct) =>
    await queries.CaptureAsync(id, ct) is { } capture
        ? Results.Ok(new
        {
            capture,
            fetches = await queries.FetchesAsync(id, ct),
            passes = await queries.PassesForCaptureAsync(id, ct),
            runs = await queries.RunsForCaptureAsync(id, ct),
        })
        : Results.NotFound(new { error = $"No retrieve {id}." }));

api.MapGet("/runs/{id:int}", async (int id, CancellationToken ct) =>
    await queries.RunAsync(id, ct) is { } run
        ? Results.Ok(new { run, pass = await queries.PassAsync(run.PassId, ct) })
        : Results.NotFound(new { error = $"No run {id}." }));

api.MapGet("/runs/{id:int}/candidates", (int id, string? q, int? offset, int? limit, CancellationToken ct) =>
    queries.CandidatesAsync(id, q, Math.Max(0, offset ?? 0), Math.Clamp(limit ?? 100, 1, 500), ct));

api.MapGet("/runs/{id:int}/measures", (int id, int? offset, int? limit, CancellationToken ct) =>
    queries.MeasuresAsync(id, Math.Max(0, offset ?? 0), Math.Clamp(limit ?? 100, 1, 500), ct));

api.MapGet("/runs/{id:int}/county-ballots", (int id, CancellationToken ct) => queries.CountyBallotsAsync(id, ct));

api.MapGet("/activity", async (CancellationToken ct) =>
{
    var passes = await queries.RecentPassesAsync(50, ct);
    return new
    {
        captures = await queries.RecentCapturesAsync(50, ct),
        passes,
        runs = await queries.RunsForPassesAsync(passes.Select(p => p.PassId), ct),
    };
});

api.MapGet("/jobs", async (CancellationToken ct) =>
{
    var recent = await jobs.RecentAsync(50, ct);
    return new
    {
        jobs = recent,
        runs = await queries.RunsForPassesAsync(recent.Where(j => j.PassId is not null).Select(j => j.PassId!.Value), ct),
    };
});

api.MapGet("/jobs/{id:int}", async (int id, CancellationToken ct) =>
    await jobs.GetAsync(id, ct) is { } job ? Results.Ok(job) : Results.NotFound(new { error = $"No job {id}." }));

// The console calls a capture a "retrieve", so the messages a person reads here do too.
// Queues a run and returns at once. Everything that can be refused is refused here, so a queued job is one that can start.
api.MapPost("/jobs", async (StartJob body, CancellationToken ct) =>
{
    IResult Refuse(string error) => Results.BadRequest(new { error });

    var state = (body.StateCode ?? "").Trim().ToUpperInvariant();
    var catalog = StateCatalog.LoadFromDataRoot(dataRoot);
    if (!catalog.TryGet(state, out var entry))
        return Refuse($"No state '{state}' in the catalog.");
    if (!StateCatalog.IsImplemented(entry.Status) || !CollectorDiscovery.Discover().ContainsKey(state))
        return Refuse($"{entry.Name} has no collector yet.");

    var kind = body.Kind;
    if (kind is not (JobStore.Capture or JobStore.Normalize or JobStore.Both))
        return Refuse($"Kind must be {JobStore.Capture}, {JobStore.Normalize} or {JobStore.Both}.");

    var requestedBy = (body.RequestedBy ?? "").Trim();
    if (requestedBy.Length is 0 or > 100)
        return Refuse("Enter your name (up to 100 characters). It is recorded on the run.");

    string? election = null;
    if (kind != JobStore.Capture && !string.IsNullOrWhiteSpace(body.ElectionFilter))
    {
        if (!DateOnly.TryParseExact(body.ElectionFilter, "yyyy-MM-dd", out _))
            return Refuse($"The election date must be yyyy-MM-dd, got '{body.ElectionFilter}'.");
        election = body.ElectionFilter;
    }

    int year;
    int? normalizeCaptureId = null;
    if (kind == JobStore.Normalize)
    {
        if (body.NormalizeCaptureId is not { } captureId)
            return Refuse("Choose the retrieve to normalize.");
        var capture = await queries.CaptureAsync(captureId, ct);
        if (capture is null)
            return Refuse($"No retrieve {captureId}.");
        if (capture.StateCode != state)
            return Refuse($"Retrieve {captureId} is for {capture.StateCode}, not {state}.");
        if (capture.Status != "succeeded")
            return Refuse($"Retrieve {captureId} is {capture.Status}. Only a succeeded retrieve can be normalized.");
        if (!capture.FilesPresent)
            return Refuse($"Retrieve {captureId}'s payloads are no longer on disk, so it cannot be normalized.");
        normalizeCaptureId = captureId;
        year = capture.Year;
    }
    else
    {
        if (body.Year is not (>= 2000 and <= 2100))
            return Refuse("Enter the election year.");
        year = body.Year.Value;
    }

    if (await jobs.ActiveForStateAsync(state, ct) is { } active)
        return Results.Conflict(new { error = $"{entry.Name} already has job {active.JobId} {active.Status}. Wait for it to finish." });

    var jobId = await jobs.EnqueueAsync(new JobStore.NewJob(state, year, kind, normalizeCaptureId, election, requestedBy), ct);
    return Results.Accepted($"/api/jobs/{jobId}", await jobs.GetAsync(jobId, ct));
});

app.Run();

internal sealed record StartJob(
    string? StateCode, int? Year, string? Kind, int? NormalizeCaptureId, string? ElectionFilter, string? RequestedBy);
