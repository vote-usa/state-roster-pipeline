using System.Globalization;

namespace StateBallot.Staging;

/// <summary>
/// A "ruleset" step runs the linked ruleset's current steps. A pipeline has to be expanded
/// (<see cref="Expand"/>) before it is evaluated.
/// </summary>
public static class RulesetSteps
{
    public const string IdParam = "rulesetId";

    public const int MaxSamples = 1000;

    public sealed record SampleResult(string? Value, string? Error);

    /// <summary>Evaluates each sample as the only input. <paramref name="steps"/> must be cleaned and expanded.</summary>
    public static List<SampleResult> EvaluateSamples(
        IMappingEvaluator evaluator, List<TransformStep> steps, IEnumerable<string?> samples, CancellationToken ct = default)
    {
        var noRow = new Dictionary<string, string>();
        var results = new List<SampleResult>();
        foreach (var sample in samples)
        {
            ct.ThrowIfCancellationRequested();
            var mapping = ResolvedMapping.OfValues([sample], steps);
            try
            {
                results.Add(new SampleResult(evaluator.Evaluate(mapping, noRow), null));
            }
            catch (Exception ex)
            {
                results.Add(new SampleResult(null, ex.Message));
            }
        }
        return results;
    }

    public static bool TryParseId(string? value, out int id) =>
        int.TryParse(value?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out id) && id > 0;

    /// <summary>Direct links only. Expects cleaned steps.</summary>
    public static IEnumerable<int> Links(IEnumerable<TransformStep> steps) =>
        steps.Where(s => s.Kind == TransformKinds.Ruleset).Select(Id);

    /// <summary>The rulesets a mapping references, at the target and on its inputs.</summary>
    public static IEnumerable<int> RulesetIds(TargetColumnMapping mapping) =>
        (mapping.RulesetIds ?? []).Concat((mapping.Inputs ?? []).SelectMany(i => i.RulesetIds ?? []));

    /// <summary>
    /// Replaces ruleset steps, at any depth, with the steps they link to. Throws for a missing ruleset or a
    /// loop. <paramref name="ownerId"/> is the saved ruleset these steps belong to, so a link back to it is a loop.
    /// </summary>
    public static List<TransformStep> Expand(
        IEnumerable<TransformStep> steps, IReadOnlyDictionary<int, Ruleset> rulesets, int? ownerId = null)
    {
        var path = new List<int>();
        if (ownerId is { } owner)
            path.Add(owner);
        return Expand(steps, rulesets, path);
    }

    /// <summary>The steps each level of the mapping runs now, ready to evaluate.</summary>
    public static ResolvedMapping Resolve(TargetColumnMapping mapping, IReadOnlyDictionary<int, Ruleset> rulesets) => new(
        (mapping.Inputs ?? []).Select(i => new ResolvedInput(i, Expand((i.RulesetIds ?? []).Select(Link), rulesets))).ToList(),
        Expand((mapping.RulesetIds ?? []).Select(Link), rulesets));

    public static TransformStep Link(int rulesetId) => new()
    {
        Kind = TransformKinds.Ruleset,
        Params = { [IdParam] = rulesetId.ToString(CultureInfo.InvariantCulture) },
    };

    private static List<TransformStep> Expand(IEnumerable<TransformStep> steps, IReadOnlyDictionary<int, Ruleset> rulesets, List<int> path)
    {
        var expanded = new List<TransformStep>();
        foreach (var step in steps)
        {
            if (step.Kind != TransformKinds.Ruleset)
            {
                expanded.Add(step);
                continue;
            }

            var id = Id(step);
            if (path.Contains(id))
            {
                var loop = path.SkipWhile(p => p != id).Append(id).Select(p => Describe(p, rulesets));
                throw new ArgumentException($"Ruleset {Describe(id, rulesets)} would run itself: {string.Join(" → ", loop)}.");
            }
            if (!rulesets.TryGetValue(id, out var linked))
                throw new ArgumentException($"No ruleset {id}.");

            path.Add(id);
            expanded.AddRange(Expand(linked.Steps, rulesets, path));
            path.RemoveAt(path.Count - 1);
        }
        return expanded;
    }

    /// <summary>Refuses a link to a deactivated ruleset unless the pipeline being saved already had it.</summary>
    public static void RefuseNewInactiveLinks(IEnumerable<int> links, IEnumerable<int> alreadyLinked, IEnumerable<Ruleset> rulesets)
    {
        var kept = alreadyLinked.ToHashSet();
        var inactive = rulesets
            .Where(r => !r.IsActive && !kept.Contains(r.RulesetId) && links.Contains(r.RulesetId))
            .Select(r => $"'{r.Name}'")
            .ToList();
        if (inactive.Count > 0)
            throw new ArgumentException($"Ruleset {string.Join(", ", inactive)} is deactivated, so it cannot be added. Reactivate it first.");
    }

    private static int Id(TransformStep step) =>
        step.Params.TryGetValue(IdParam, out var value) && TryParseId(value, out var id)
            ? id
            : throw new ArgumentException($"A ruleset step needs a ruleset id in '{IdParam}', got '{value}'.");

    private static string Describe(int id, IReadOnlyDictionary<int, Ruleset> rulesets) =>
        rulesets.TryGetValue(id, out var r) ? $"'{r.Name}'" : id.ToString(CultureInfo.InvariantCulture);
}
