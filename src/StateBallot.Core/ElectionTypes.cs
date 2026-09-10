namespace StateBallot.Core;

/// <summary>
/// The election_type vocabulary every published row uses. Collectors keep whatever the
/// source calls an election. The writer maps it here and records the raw value in run.json.
/// </summary>
public static class ElectionTypes
{
    public const string Primary = "Primary";
    public const string General = "General";
    public const string Special = "Special";
    public const string SpecialPrimary = "Special Primary";
    public const string SpecialGeneral = "Special General";
    public const string PrimaryRunoff = "Primary Runoff";
    public const string GeneralRunoff = "General Runoff";
    public const string Other = "Other";

    public static readonly string[] All =
        [Primary, General, Special, SpecialPrimary, SpecialGeneral, PrimaryRunoff, GeneralRunoff, Other];

    private static readonly Dictionary<string, string> Exact = new(StringComparer.OrdinalIgnoreCase)
    {
        ["G"] = General, ["GE"] = General, ["GEN"] = General, ["GENERAL"] = General,
        ["P"] = Primary, ["PR"] = Primary, ["PRI"] = Primary, ["PRIMARY"] = Primary,
        ["S"] = Special, ["SP"] = Special, ["SPECIAL"] = Special,
        ["R"] = GeneralRunoff, ["RUNOFF"] = GeneralRunoff,
        ["Q"] = PrimaryRunoff,
    };

    public static string Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return Other;

        var text = raw.Trim();
        if (All.Contains(text, StringComparer.Ordinal))
            return text;
        if (Exact.TryGetValue(text, out var exact))
            return exact;

        var upper = text.ToUpperInvariant();
        var special = upper.Contains("SPECIAL");
        var runoff = upper.Contains("RUNOFF") || upper.Contains("RUN-OFF");
        var primary = upper.Contains("PRIMARY");
        var general = upper.Contains("GENERAL");

        if (runoff && primary) return PrimaryRunoff;
        if (runoff) return GeneralRunoff;
        if (special && primary) return SpecialPrimary;
        if (special && general) return SpecialGeneral;
        if (special) return Special;
        if (primary) return Primary;
        if (general) return General;
        return Other;
    }
}
