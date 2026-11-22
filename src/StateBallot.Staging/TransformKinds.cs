namespace StateBallot.Staging;

public sealed record TransformParam(string Name, bool Required, string? Default = null);

public sealed record TransformKind(string Name, string Summary, IReadOnlyList<TransformParam> Params);

// The steps a ruleset can hold, with the parameters each declares.
// Drives both save-time validation and the ruleset editor's per-step form.
public static class TransformKinds
{
    public const string Passthrough = "passthrough";
    public const string Concat = "concat";
    public const string Coalesce = "coalesce";
    public const string Trim = "trim";
    public const string Upper = "upper";
    public const string Lower = "lower";
    public const string RegexReplace = "regex_replace";
    public const string RegexExtract = "regex_extract";
    public const string Ruleset = "ruleset";

    public static IReadOnlyList<TransformKind> All { get; } =
    [
        new(Passthrough, "Use the single input unchanged.", []),
        new(Concat, "Join all inputs in order.", [new("separator", false, "")]),
        new(Coalesce, "Use the first input that is non-empty.", []),
        new(Trim, "Remove leading and trailing whitespace.", []),
        new(Upper, "Uppercase the value.", []),
        new(Lower, "Lowercase the value.", []),
        new(RegexReplace, "Replace matches of a pattern.",
            [new("pattern", true), new("replacement", true), new("ignoreCase", false, "false")]),
        new(RegexExtract, "Keep the first match (or a capture group) of a pattern.",
            [new("pattern", true), new("group", false, "0")]),
        new(Ruleset, "Run the steps of another saved ruleset.", [new(RulesetSteps.IdParam, true)]),
    ];

    private static readonly Dictionary<string, TransformKind> ByName =
        All.ToDictionary(k => k.Name, StringComparer.Ordinal);

    public static bool TryGet(string name, out TransformKind kind) => ByName.TryGetValue(name, out kind!);
}
