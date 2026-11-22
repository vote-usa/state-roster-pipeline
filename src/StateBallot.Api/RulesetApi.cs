using StateBallot.Staging;

namespace StateBallot.Api;

/// <summary>Saved transform rulesets (see <see cref="RulesetSteps"/>). Refusals answer 400 with { error }.</summary>
public static class RulesetApi
{
    public static void MapRulesetApi(this IEndpointRouteBuilder api)
    {
        IResult Refuse(string error) => Results.BadRequest(new { error });
        IResult NoRuleset(int id) => Results.NotFound(new { error = $"No ruleset {id}." });

        // The step kinds a ruleset can hold, with their parameters.
        api.MapGet("/transforms", () => Results.Ok(TransformKinds.All));

        // ?active=true drops deactivated rulesets, ?search= keeps names containing it, ?summary=true omits the steps.
        api.MapGet("/rulesets", async (bool? active, bool? summary, string? search, RulesetStore store, CancellationToken ct) =>
            summary == true
                ? Results.Ok(await store.ListSummariesAsync(active == true, search, ct))
                : Results.Ok(await store.ListAsync(active == true, search, ct)));

        // Separate from PUT /rulesets/{id}, so a full save without the flag cannot reactivate a ruleset.
        api.MapPut("/rulesets/{id:int}/active", async (int id, SetRulesetActive body, RulesetStore store, CancellationToken ct) =>
        {
            try
            {
                return await store.SetActiveAsync(id, body.IsActive, ct) is { } ruleset ? Results.Ok(ruleset) : NoRuleset(id);
            }
            catch (ArgumentException ex)
            {
                return Refuse(ex.Message);
            }
        });

        api.MapGet("/rulesets/{id:int}", async (int id, RulesetStore store, CancellationToken ct) =>
            await store.GetAsync(id, ct) is { } ruleset ? Results.Ok(ruleset) : NoRuleset(id));

        api.MapPost("/rulesets", async (Ruleset body, RulesetStore store, CancellationToken ct) =>
        {
            try
            {
                var created = await store.CreateAsync(body, ct);
                return Results.Created($"/api/rulesets/{created.RulesetId}", created);
            }
            catch (ArgumentException ex)
            {
                return Refuse(ex.Message);
            }
        });

        api.MapPut("/rulesets/{id:int}", async (int id, Ruleset body, RulesetStore store, CancellationToken ct) =>
        {
            try
            {
                return await store.UpdateAsync(id, body, ct) is { } updated ? Results.Ok(updated) : NoRuleset(id);
            }
            catch (ArgumentException ex)
            {
                return Refuse(ex.Message);
            }
        });

        api.MapDelete("/rulesets/{id:int}", async (int id, RulesetStore store, CancellationToken ct) =>
        {
            try
            {
                return await store.DeleteAsync(id, ct) ? Results.NoContent() : NoRuleset(id);
            }
            catch (ArgumentException ex)
            {
                return Refuse(ex.Message);
            }
        });

        // "values" are the inputs of one evaluation and answer { value }; "samples" each run alone and answer { results }.
        api.MapPost("/rulesets/try", async (TryRuleset body, IMappingEvaluator evaluator, RulesetStore store, CancellationToken ct) =>
        {
            if (body.Samples is not null && body.Values is not null)
                return Refuse("Send values or samples, not both.");
            if (body.Samples is { Count: > RulesetSteps.MaxSamples })
                return Refuse($"Send at most {RulesetSteps.MaxSamples} samples, got {body.Samples.Count}.");

            List<TransformStep> steps;
            try
            {
                steps = RulesetStore.CleanSteps(body.Steps);
                if (RulesetSteps.Links(steps).Any())
                    steps = RulesetSteps.Expand(steps, await store.ByIdAsync(ct));
            }
            catch (ArgumentException ex)
            {
                return Refuse(ex.Message);
            }

            if (body.Samples is not null)
                return Results.Ok(new { results = RulesetSteps.EvaluateSamples(evaluator, steps, body.Samples, ct) });

            var mapping = ResolvedMapping.OfValues(body.Values ?? [], steps);
            try
            {
                return Results.Ok(new { value = evaluator.Evaluate(mapping, new Dictionary<string, string>()) });
            }
            catch (Exception ex) when (ex is ArgumentException or System.Text.RegularExpressions.RegexMatchTimeoutException)
            {
                return Refuse(ex.Message);
            }
        });
    }
}

public sealed record SetRulesetActive(bool IsActive);

public sealed record TryRuleset(List<TransformStep>? Steps, List<string?>? Values, List<string?>? Samples);
