using System.Globalization;
using System.Text.RegularExpressions;

namespace StateBallot.Staging;

public interface IMappingEvaluator
{
    string Evaluate(TargetColumnMapping mapping, IReadOnlyDictionary<string, string> row);
}

// Produces a target column's value from one source row by resolving its inputs and running its
// transform pipeline. This is the single evaluator used both by the editor's preview and by an
// actual conversion run - the behaviour a saved mapping promises.
public sealed class MappingEvaluator : IMappingEvaluator
{
    public string Evaluate(TargetColumnMapping mapping, IReadOnlyDictionary<string, string> row)
    {
        var values = mapping.Inputs.Select(input => EvaluateInput(input, row)).ToList();
        return Reduce(RunPipeline(mapping.Transform, values));
    }

    private static string EvaluateInput(MappingInput input, IReadOnlyDictionary<string, string> row) =>
        Reduce(RunPipeline(input.Transform, [Resolve(input, row)]));

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
            default:
                return values; // unknown kinds cannot be saved, so this is unreachable in practice
        }
    }

    private static string Param(TransformStep step, string name, string fallback) =>
        step.Params.TryGetValue(name, out var value) && value is not null ? value : fallback;

    private static bool IsTrue(string value) => string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
}
