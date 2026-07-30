using System.Globalization;
using System.Text.RegularExpressions;

namespace StateBallot.Core;

/// <summary>
/// Builds Open Civic Data division identifiers from roster fields.
/// Returns null when the jurisdiction cannot be mapped confidently
/// (e.g. an underspecified local special election).
/// See https://github.com/opencivicdata/ocd-division-ids
/// </summary>
public static partial class OcdDivisionId
{
    private static readonly Regex Digits = DigitsRegex();
    private static readonly Regex CountySuffix = CountySuffixRegex();
    private static readonly Regex CongressionalDistrictPattern = CongressionalDistrictRegex();
    private static readonly Regex StateSenateDistrictPattern = StateSenateDistrictRegex();
    private static readonly Regex StateAssemblyDistrictPattern = StateAssemblyDistrictRegex();

    public static string State(string stateCode) =>
        $"ocd-division/country:us/state:{NormalizeState(stateCode)}";

    public static string County(string stateCode, string countyName)
    {
        var name = CountySuffix.Replace(countyName.Trim(), "");
        return $"{State(stateCode)}/county:{Slug(name)}";
    }

    public static string CongressionalDistrict(string stateCode, string district) =>
        $"{State(stateCode)}/cd:{NormalizeDistrict(district)}";

    public static string StateSenateDistrict(string stateCode, string district) =>
        $"{State(stateCode)}/sldu:{NormalizeDistrict(district)}";

    public static string StateHouseDistrict(string stateCode, string district) =>
        $"{State(stateCode)}/sldl:{NormalizeDistrict(district)}";

    /// <summary>Division for a candidate row from office + district + county.</summary>
    public static string? ForCandidate(string state, string office, string? district, string? county)
    {
        if (string.IsNullOrWhiteSpace(state))
            return null;

        var officeKey = NormalizeKey(office);

        if (IsUsHouse(officeKey) && HasDistrict(district))
            return CongressionalDistrict(state, district!);

        if (IsStateSenate(officeKey) && HasDistrict(district))
            return StateSenateDistrict(state, district!);

        if (IsStateHouse(officeKey) && HasDistrict(district))
            return StateHouseDistrict(state, district!);

        // US Senate and statewide executive / judicial offices.
        if (IsStatewideOffice(officeKey))
            return State(state);

        // Local race attributed to one county (or "; "-joined list — use first only if single).
        if (!string.IsNullOrWhiteSpace(county) && !county.Contains(';'))
            return County(state, county);

        // Districted office we don't recognize, or multi-county local: leave null.
        if (HasDistrict(district))
            return null;

        return State(state);
    }

    /// <summary>Division for a measure from jurisdiction + county.</summary>
    public static string? ForMeasure(string state, string jurisdiction, string? county)
    {
        if (!string.IsNullOrWhiteSpace(county) && !county.Contains(';'))
            return County(state, county);

        return ForJurisdiction(state, jurisdiction);
    }

    /// <summary>Division for an election from its jurisdiction string.</summary>
    public static string? ForElection(string state, string jurisdiction, string? name = null)
    {
        var fromJurisdiction = ForJurisdiction(state, jurisdiction);
        if (fromJurisdiction is not null)
            return fromJurisdiction;

        // Fall back to parsing the election name (e.g. "Congressional District 14, Special...").
        if (!string.IsNullOrWhiteSpace(name))
            return ForJurisdiction(state, name);

        return null;
    }

    public static string? ForCounty(string state, string countyName) =>
        string.IsNullOrWhiteSpace(countyName) ? null : County(state, countyName);

    private static string? ForJurisdiction(string state, string? jurisdiction)
    {
        if (string.IsNullOrWhiteSpace(jurisdiction))
            return null;

        var text = jurisdiction.Trim();
        if (string.Equals(text, "state", StringComparison.OrdinalIgnoreCase))
            return State(state);

        var cd = CongressionalDistrictPattern.Match(text);
        if (cd.Success)
            return CongressionalDistrict(state, cd.Groups["n"].Value);

        var sldu = StateSenateDistrictPattern.Match(text);
        if (sldu.Success)
            return StateSenateDistrict(state, sldu.Groups["n"].Value);

        var sldl = StateAssemblyDistrictPattern.Match(text);
        if (sldl.Success)
            return StateHouseDistrict(state, sldl.Groups["n"].Value);

        if (CountySuffix.IsMatch(text) ||
            text.EndsWith(" County", StringComparison.OrdinalIgnoreCase))
            return County(state, text);

        return null;
    }

    private static bool HasDistrict(string? district) =>
        !string.IsNullOrWhiteSpace(district) && Digits.IsMatch(district);

    private static string NormalizeState(string stateCode) =>
        stateCode.Trim().ToLowerInvariant();

    private static string NormalizeDistrict(string district)
    {
        var match = Digits.Match(district);
        if (!match.Success)
            throw new ArgumentException($"No digits in district '{district}'.");
        // OCD district ids are unpadded integers as strings ("14", not "014").
        return int.Parse(match.Value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);
    }

    private static string Slug(string name)
    {
        var parts = name.Trim().ToLowerInvariant()
            .Replace('–', ' ').Replace('—', ' ')
            .Replace('.', ' ')
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return string.Join('_', parts);
    }

    private static string NormalizeKey(string office) =>
        string.Join(' ', office.ToLowerInvariant()
            .Replace('.', ' ')
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static bool IsUsHouse(string officeKey) =>
        officeKey.Contains("united states representative", StringComparison.Ordinal) ||
        officeKey.Contains("u s representative", StringComparison.Ordinal) ||
        officeKey.Contains("us representative", StringComparison.Ordinal) ||
        officeKey.Contains("representative in congress", StringComparison.Ordinal) ||
        officeKey.Equals("congress", StringComparison.Ordinal);

    private static bool IsStateSenate(string officeKey) =>
        officeKey.Contains("state senator", StringComparison.Ordinal) ||
        officeKey.Contains("state senate", StringComparison.Ordinal);

    private static bool IsStateHouse(string officeKey) =>
        officeKey.Contains("state assembly", StringComparison.Ordinal) ||
        officeKey.Contains("member of the state assembly", StringComparison.Ordinal) ||
        officeKey.Contains("member of the assembly", StringComparison.Ordinal) ||
        officeKey.Contains("assemblymember", StringComparison.Ordinal) ||
        officeKey.Contains("assembly member", StringComparison.Ordinal);

    private static bool IsStatewideOffice(string officeKey)
    {
        if (officeKey.Contains("united states senator", StringComparison.Ordinal) ||
            officeKey.Contains("u s senator", StringComparison.Ordinal) ||
            officeKey.Contains("us senator", StringComparison.Ordinal))
            return true;

        string[] statewide =
        [
            "governor", "lieutenant governor", "secretary of state", "controller",
            "state controller", "treasurer", "state treasurer", "attorney general",
            "insurance commissioner", "superintendent of public instruction",
            "board of equalization",
        ];
        return statewide.Any(s => officeKey.Equals(s, StringComparison.Ordinal) ||
                                  officeKey.StartsWith(s + " ", StringComparison.Ordinal));
    }

    [GeneratedRegex(@"\d+", RegexOptions.Compiled)]
    private static partial Regex DigitsRegex();

    [GeneratedRegex(@"\s+County\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex CountySuffixRegex();

    [GeneratedRegex(@"Congressional\s+District\s+(?<n>\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex CongressionalDistrictRegex();

    [GeneratedRegex(@"(?:State\s+)?Senat(?:e|or)\s+District\s+(?<n>\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex StateSenateDistrictRegex();

    [GeneratedRegex(@"(?:State\s+)?Assembly\s+District\s+(?<n>\d+)", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex StateAssemblyDistrictRegex();
}
