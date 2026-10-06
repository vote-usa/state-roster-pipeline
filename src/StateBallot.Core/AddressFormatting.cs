namespace StateBallot.Core;

/// <summary>Shared address-field formatting for state collectors.</summary>
public static class AddressFormatting
{
    /// <summary>
    /// Joins non-blank address-line parts, in the order given, with a single
    /// space; returns null if every part is blank. Core has no dependency on any
    /// state's address DTO - callers pass the raw field values.
    /// </summary>
    public static string? FormatMailingLine(params string?[] parts)
    {
        var nonBlank = parts.Where(p => !string.IsNullOrWhiteSpace(p)).ToList();
        return nonBlank.Count == 0 ? null : string.Join(" ", nonBlank);
    }
}
