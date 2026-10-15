using StateBallot.Staging;

namespace StateBallot.Api;

public static class MappingApi
{
    public static void MapMappingApi(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/targets", () =>
            Results.Ok(CandidateTargetColumns.All.Select(c => new { c.Name, c.Required })));

        app.MapGet("/api/transforms", () => Results.Ok(TransformKinds.All));

        // Runs the current (possibly unsaved) mapping against one source row, using the real evaluator.
        app.MapPost("/api/preview", (PreviewRequest req, IMappingEvaluator evaluator) =>
        {
            var row = req.Row ?? new Dictionary<string, string>();
            var results = (req.Columns ?? []).Select(c =>
            {
                try
                {
                    return new { targetColumn = c.TargetColumn, value = (string?)evaluator.Evaluate(c, row), error = (string?)null };
                }
                catch (Exception ex)
                {
                    return new { targetColumn = c.TargetColumn, value = (string?)null, error = (string?)ex.Message };
                }
            });
            return Results.Ok(results);
        });

        app.MapGet("/api/source", async (string state, int? year, ISourceLoader loader, CancellationToken ct) =>
        {
            try
            {
                var sample = await loader.LoadAsync(state, year ?? DateTime.UtcNow.Year, ct);
                return Results.Ok(sample);
            }
            catch (NotSupportedException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                return Results.Problem($"Could not load {state} source data: {ex.Message}");
            }
        });

        app.MapGet("/api/groups", async (string? state, ColumnMappingStore store, CancellationToken ct) =>
            Results.Ok(await store.ListGroupsAsync(state, ct)));

        app.MapPost("/api/groups", async (MappingGroup group, ColumnMappingStore store, CancellationToken ct) =>
        {
            try
            {
                return Results.Ok(await store.CreateGroupAsync(group, ct));
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        app.MapPost("/api/groups/{id:int}/clone", async (int id, MappingGroup target, ColumnMappingStore store, CancellationToken ct) =>
        {
            try
            {
                return Results.Ok(await store.CloneGroupAsync(id, target, ct));
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });

        app.MapGet("/api/groups/{id:int}/columns", async (int id, ColumnMappingStore store, CancellationToken ct) =>
        {
            if (await store.GetGroupAsync(id, ct) is null)
                return Results.NotFound(new { error = $"No mapping group {id}." });
            return Results.Ok(await store.GetColumnsAsync(id, ct));
        });

        app.MapPut("/api/groups/{id:int}/columns", async (int id, List<TargetColumnMapping> columns, ColumnMappingStore store, CancellationToken ct) =>
        {
            try
            {
                await store.SaveColumnsAsync(id, columns, ct);
                return Results.Ok(await store.GetColumnsAsync(id, ct));
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        });
    }
}

public record PreviewRequest(List<TargetColumnMapping> Columns, Dictionary<string, string> Row);
