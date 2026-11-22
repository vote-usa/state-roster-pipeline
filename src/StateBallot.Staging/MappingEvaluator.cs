using System.Globalization;
using System.Text.RegularExpressions;

namespace StateBallot.Staging;

/// <summary>An input with the steps its rulesets run, in order.</summary>
public sealed record ResolvedInput(MappingInput Input, List<TransformStep> Steps);

/// <summary>A mapping with its rulesets replaced by the steps they run (<see cref="RulesetSteps.Resolve"/>).</summary>
public sealed record ResolvedMapping(List<ResolvedInput> Inputs, List<TransformStep> Steps)
{
    /// <summary>Manual-value inputs with no steps of their own, run through <paramref name="steps"/>.</summary>
    public static ResolvedMapping OfValues(IEnumerable<string?> values, List<TransformStep> steps) => new(
        values.Select(v => new ResolvedInput(new MappingInput { Kind = MappingInputKinds.Manual, ManualValue = v ?? "" }, [])).ToList(),
        steps);
}

public interface IMappingEvaluator
{
    string Evaluate(ResolvedMapping mapping, IReadOnlyDictionary<string, string> row);
}

// Produces a target column's value from one source row: each input runs its own steps, then the
// target's steps run on all of them. The single evaluator used both by the editor's preview and by an
// actual conversion run - the behaviour a saved mapping promises.
public sealed class MappingEvaluator : IMappingEvaluator
{
    public string Evaluate(ResolvedMapping mapping, IReadOnlyDictionary<string, string> row)
    {
        var values = mapping.Inputs.Select(input => EvaluateInput(input, row)).ToList();
        return Reduce(RunPipeline(mapping.Steps, values));
    }

    private static string EvaluateInput(ResolvedInput input, IReadOnlyDictionary<string, string> row) =>
        Reduce(RunPipeline(input.Steps, [Resolve(input.Input, row)]));

    private static List<string> RunPipeline(List<TransformStep> steps, List<string> values)
    {
        foreach (var step in steps)
            values = Apply(step, values);
        return values;
    }

    private static string Reduce(List<string> values) =>
        values.Count switch { 0 => "", 1 => values[0], _ => string.Concat(values) };

    private static string Resolve(MappingInput input, IReadOnlyDictionary<string, string> row)
    {
        if (input.Kind == MappingInputKinds.Manual)
            return input.ManualValue ?? "";
        return input.SourceColumn is { } col && row.TryGetValue(col, out var value) && value is not null ? value : "";
    }

    private static List<string> Apply(TransformStep step, List<string> values)
    {
        switch (step.Kind)
        {
            case TransformKinds.Concat:
                return [string.Join(Param(step, "separator", ""), values)];
            case TransformKinds.Coalesce:
                return [values.FirstOrDefault(v => !string.IsNullOrEmpty(v)) ?? ""];
            case TransformKinds.Passthrough:
                return values;
            case TransformKinds.Trim:
                return values.Select(v => v.Trim()).ToList();
            case TransformKinds.Upper:
                return values.Select(v => v.ToUpperInvariant()).ToList();
            case TransformKinds.Lower:
                return values.Select(v => v.ToLowerInvariant()).ToList();
            case TransformKinds.RegexReplace:
            {
                var options = IsTrue(Param(step, "ignoreCase", "false")) ? RegexOptions.IgnoreCase : RegexOptions.None;
                var regex = new Regex(Param(step, "pattern", ""), options);
                var replacement = Param(step, "replacement", "");
                return values.Select(v => regex.Replace(v, replacement)).ToList();
            }
            case TransformKinds.RegexExtract:
            {
                var regex = new Regex(Param(step, "pattern", ""));
                var group = int.TryParse(Param(step, "group", "0"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var g) ? g : 0;
                return values.Select(v =>
                {
                    var match = regex.Match(v);
                    return match.Success && group >= 0 && group < match.Groups.Count ? match.Groups[group].Value : "";
                }).ToList();
            }
            case TransformKinds.Ruleset:
                throw new InvalidOperationException(
                    "A ruleset step has to be expanded with RulesetSteps.Expand before the pipeline is evaluated.");
            default:
                return values; // unknown kinds cannot be saved, so this is unreachable in practice
        }
    }

    private static string Param(TransformStep step, string name, string fallback) =>
        step.Params.TryGetValue(name, out var value) && value is not null ? value : fallback;

    private static bool IsTrue(string value) => string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
}
