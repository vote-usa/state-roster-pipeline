using System.Text.RegularExpressions;

namespace StateBallot.States.Hi;

/// <summary>Every regex used against HI pages and CSV cell values, centralized so drift only requires updating this file.</summary>
public static class HiSelectors
{
    // --- elections.hawaii.gov home page's "20NN Elections" widget ---
    public static readonly Regex PrimaryDate =
        new(@"Primary:\s*\w+,\s*(?<date>[A-Za-z]+ \d{1,2}, \d{4})", RegexOptions.Compiled);

    public static readonly Regex GeneralDate =
        new(@"General:\s*\w+,\s*(?<date>[A-Za-z]+ \d{1,2}, \d{4})", RegexOptions.Compiled);

    // --- olvr.hawaii.gov CandidateFiling.aspx "ddlElection" dropdown ---
    public const string ElectionDropdownSelector = "#cphFooter_ddlElection";

    /// <summary>The dropdown's option text for a given year, e.g. "2026 Candidate Report".</summary>
    public static string ElectionReportLabel(int year) => $"{year} Candidate Report";

    // --- exported candidate CSV ---
    /// <summary>Splits "STATE SENATOR, DIST 2" into office + district (also matches "DIST 18 VACANCY", "DIST II").</summary>
    public static readonly Regex ContestWithDistrict =
        new(@"^(?<office>.+?),\s*DIST\s+(?<district>.+)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>Splits "HONOLULU, HI 96808" into city/state/zip.</summary>
    public static readonly Regex CityStateZip =
        new(@"^(?<city>.+?),\s*(?<state>[A-Z]{2})\s+(?<zip>\d{5}(-\d{4})?)$", RegexOptions.Compiled);
}
