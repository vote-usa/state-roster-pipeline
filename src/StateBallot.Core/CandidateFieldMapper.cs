namespace StateBallot.Core;

/// <summary>
/// Applies a state's candidate_field_map.json (canonical CandidateRow field
/// name -&gt; that state's own raw column name) to one parsed row. Covers only
/// the subset of CandidateRow fields that are ever a single source column's
/// value passed through verbatim (trimmed, blank-to-null) - never
/// split/composed/coalesced/synthesized. Office, District, and CandidateName
/// are deliberately never looked up this way, even for a state whose source
/// happens not to need a transform for one of them: every state onboarded so
/// far needs at least conditional logic for Office/District (a split regex,
/// even where it's a no-op on non-matching input), and CandidateName is a
/// composite (first + last name columns) for at least one state - both stay
/// hand-written per state rather than pretending they're plain lookups.
/// </summary>
public static class CandidateFieldMapper
{
    /// <summary>
    /// Looks up <paramref name="canonicalField"/> (pass a CandidateRow property
    /// name via <c>nameof(...)</c>, e.g. <c>nameof(CandidateRow.Party)</c>, so a
    /// rename is caught at compile time) in <paramref name="fieldMap"/> to find
    /// which raw column holds it for this state, then returns that column's
    /// trimmed value from <paramref name="row"/> - or null if either this state
    /// doesn't map the field at all (not published, or needs a transform and is
    /// handled elsewhere in the mapper instead) or the row's own value is blank.
    /// </summary>
    public static string? Get(IReadOnlyDictionary<string, string> fieldMap, IReadOnlyDictionary<string, string> row, string canonicalField)
    {
        if (!fieldMap.TryGetValue(canonicalField, out var column))
            return null;
        var value = row.GetValueOrDefault(column, "");
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
