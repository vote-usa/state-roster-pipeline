using Microsoft.AspNetCore.Diagnostics;
using MySqlConnector;
using StateBallot.Core;
using StateBallot.Staging;

// Read API for the run console in web/. Local only: no auth, so it listens on localhost unless told otherwise.
var builder = WebApplication.CreateBuilder(args);
if (string.IsNullOrEmpty(builder.Configuration["urls"]))
    builder.WebHost.UseUrls("http://localhost:5080");

var app = builder.Build();

var db = StagingDb.FromEnvironment();
var dataRoot = CollectorRunner.FindDataRoot();
var queries = new StagingQueries(db, dataRoot);
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

    return Results.Ok(new
    {
        code,
        name = entry.Name,
        implemented = StateCatalog.IsImplemented(entry.Status),
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
        : Results.NotFound(new { error = $"No capture {id}." }));

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

app.Run();
