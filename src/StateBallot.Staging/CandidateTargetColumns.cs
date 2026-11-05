using System.Reflection;
using StateBallot.Core;

namespace StateBallot.Staging;

public sealed record TargetColumn(string Name, bool Required);

// The target columns of a candidate mapping, derived from CandidateRow so they track the model.
// A non-nullable property is Required; a nullable one is optional.
public static class CandidateTargetColumns
{
    public static IReadOnlyList<TargetColumn> All { get; } = Build();

    private static readonly IReadOnlySet<string> NamesSet =
        All.Select(c => c.Name).ToHashSet(StringComparer.Ordinal);

    public static bool Contains(string name) => NamesSet.Contains(name);

    private static IReadOnlyList<TargetColumn> Build()
    {
        var nullability = new NullabilityInfoContext();
        return typeof(CandidateRow)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.CanRead && p.CanWrite)
            .Select(p => new TargetColumn(
                p.Name,
                Required: nullability.Create(p).WriteState == NullabilityState.NotNull))
            .ToList();
    }
}
