using System.Text.RegularExpressions;

namespace StateBallot.States.Vt;

/// <summary>Every regex used against VT SOS page text and XLSX cell values, centralized so drift only requires updating this file.</summary>
public static class VtSelectors
{
    /// <summary>Matches the candidates page's "Primary Election - Tuesday, August 11, 2026" plain-text line.</summary>
    public static readonly Regex PrimaryElectionDateLine =
        new(@"Primary\s+Election\s*-\s*(?:\w+,\s*)?(?<date>[A-Za-z]+ \d{1,2}, \d{4})", RegexOptions.Compiled);

    /// <summary>Matches the candidates page's "General Election - Tuesday, November 3, 2026" plain-text line.</summary>
    public static readonly Regex GeneralElectionDateLine =
        new(@"General\s+Election\s*-\s*(?:\w+,\s*)?(?<date>[A-Za-z]+ \d{1,2}, \d{4})", RegexOptions.Compiled);

    /// <summary>
    /// The XLSX "District Name" column doubles as county name for these
    /// county-elected row offices (see VtCandidateMapper) - Justice of the
    /// Peace's own "District Name" is a town, not a county, so it's
    /// deliberately excluded and kept in District verbatim (CandidateRow has
    /// no town-level field).
    /// </summary>
    public static readonly string[] CountyLevelOffices =
        ["STATE'S ATTORNEY", "SHERIFF", "PROBATE JUDGE", "ASSISTANT JUDGE", "HIGH BAILIFF"];

    /// <summary>"N/A" marks a statewide/at-large office - not a real district.</summary>
    public static bool IsNoDistrictMarker(string district) =>
        string.Equals(district, "N/A", StringComparison.OrdinalIgnoreCase);
}
