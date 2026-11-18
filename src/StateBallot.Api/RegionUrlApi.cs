using System.Security.Claims;
using StateBallot.Core;
using StateBallot.Staging;

namespace StateBallot.Api;

/// <summary>
/// The URLs a person keeps on a state's page in the console: for the state, or for counties and a
/// city in it. Refusals answer 400 with { error }, like POST /jobs, and an unknown state or URL 404.
/// Every change records the signed-in user as its editor.
/// </summary>
public static class RegionUrlApi
{
    public static void MapRegionUrlApi(this RouteGroupBuilder api, RegionUrlStore urls, string dataRoot)
    {
        IResult Refuse(string error) => Results.BadRequest(new { error });
        IResult NoUrl(int id) => Results.NotFound(new { error = $"No URL {id}." });

        // The state's code, or null when the catalog has no such state.
        string? KnownState(string? code)
        {
            var state = (code ?? "").Trim().ToUpperInvariant();
            return StateCatalog.LoadFromDataRoot(dataRoot).TryGet(state, out _) ? state : null;
        }

        // Cleans the request, or says why it can't be saved.
        (RegionUrlStore.SaveUrl? Url, IResult? Refusal) Check(SaveUrlBody body, ClaimsPrincipal user)
        {
            if (KnownState(body.StateCode) is not { } state)
                return (null, Refuse($"No state '{body.StateCode}' in the catalog."));
            try
            {
                return (RegionUrlStore.Clean(new RegionUrlStore.SaveUrl(
                    state, body.Counties ?? [], body.City, body.Url ?? "", body.UrlType ?? "", user.UserName())), null);
            }
            catch (ArgumentException ex)
            {
                return (null, Refuse(ex.Message));
            }
        }

        api.MapGet("/states/{code}/urls", async (string code, CancellationToken ct) =>
            KnownState(code) is { } state
                ? Results.Ok(new { urls = await urls.ListAsync(state, ct), counties = await urls.CountiesAsync(state, ct) })
                : Results.NotFound(new { error = $"No state {code} in the catalog." }));

        api.MapGet("/states/{code}/urls/history", async (string code, CancellationToken ct) =>
            KnownState(code) is { } state
                ? Results.Ok(await urls.StateHistoryAsync(state, ct))
                : Results.NotFound(new { error = $"No state {code} in the catalog." }));

        api.MapPost("/states/{code}/urls", async (string code, SaveUrlBody body, ClaimsPrincipal user, CancellationToken ct) =>
        {
            if (!string.IsNullOrWhiteSpace(body.StateCode) && !string.Equals(body.StateCode.Trim(), code, StringComparison.OrdinalIgnoreCase))
                return Refuse($"The URL is for {body.StateCode}, but was sent to {code}.");
            var (url, refusal) = Check(body with { StateCode = code }, user);
            if (refusal is not null)
                return refusal;
            try
            {
                var added = await urls.AddAsync(url!, ct);
                return Results.Created($"/api/urls/{added.UrlId}", added);
            }
            catch (ArgumentException ex)
            {
                return Refuse(ex.Message);
            }
        });

        api.MapPut("/urls/{id:int}", async (int id, SaveUrlBody body, ClaimsPrincipal user, CancellationToken ct) =>
        {
            var (url, refusal) = Check(body, user);
            if (refusal is not null)
                return refusal;
            try
            {
                return await urls.UpdateAsync(id, url!, ct) is { } saved ? Results.Ok(saved) : NoUrl(id);
            }
            catch (ArgumentException ex)
            {
                return Refuse(ex.Message);
            }
        });

        api.MapDelete("/urls/{id:int}", async (int id, ClaimsPrincipal user, CancellationToken ct) =>
        {
            if (await urls.DeleteAsync(id, user.UserName(), ct))
                return Results.NoContent();
            return await urls.GetAsync(id, ct) is null ? NoUrl(id) : Refuse($"URL {id} is already deleted.");
        });

        api.MapGet("/urls/{id:int}/history", async (int id, CancellationToken ct) =>
            await urls.GetAsync(id, ct) is null ? NoUrl(id) : Results.Ok(await urls.HistoryAsync(id, ct)));

        api.MapPost("/urls/{id:int}/revert", async (int id, RevertBody body, ClaimsPrincipal user, CancellationToken ct) =>
        {
            if (body.VersionId is not { } versionId)
                return Refuse("Choose the version to revert to.");
            try
            {
                return await urls.RevertAsync(id, versionId, user.UserName(), ct) is { } reverted ? Results.Ok(reverted) : NoUrl(id);
            }
            catch (ArgumentException ex)
            {
                return Refuse(ex.Message);
            }
        });
    }

    internal sealed record SaveUrlBody(
        string? StateCode, IReadOnlyList<string>? Counties, string? City, string? Url, string? UrlType);

    internal sealed record RevertBody(int? VersionId);
}
