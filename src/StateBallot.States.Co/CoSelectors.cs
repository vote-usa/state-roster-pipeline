using System.Text.RegularExpressions;

namespace StateBallot.States.Co;

/// <summary>Every regex/CSS selector used against CO SOS pages, PDF text, and XLSX cell values, centralized so drift only requires updating this file.</summary>
public static class CoSelectors
{
    /// <summary>CSS selector for the "Excel version (XLSX)" link on a candidate list page.</summary>
    public const string XlsxLinkCss = "a[href$='.xlsx']";

    /// <summary>
    /// Matches a candidate list page's own heading, e.g. "2026 General Election
    /// Unofficial Candidate List" / "2026 Official Primary Election Candidate
    /// List" - the year is cross-checked against what the collector requested,
    /// since the page URL itself isn't year-parameterized.
    /// </summary>
    public static readonly Regex CandidateListHeading =
        new(@"(?<year>\d{4}).*?(?:Primary|General)\s+Election.*?Candidate\s+List", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Matches the election calendar PDF's "Primary Election: June 30, 2026" header line.</summary>
    public static readonly Regex PrimaryElectionDateLine =
        new(@"Primary\s+Election:\s*(?<date>[A-Za-z]+ \d{1,2}, \d{4})", RegexOptions.Compiled);

    /// <summary>Matches the election calendar PDF's "General Election: November 3, 2026" header line.</summary>
    public static readonly Regex GeneralElectionDateLine =
        new(@"General\s+Election:\s*(?<date>[A-Za-z]+ \d{1,2}, \d{4})", RegexOptions.Compiled);

    /// <summary>
    /// The XLSX "District" column doubles as county name for county-court
    /// races (see CoCandidateMapper) - these are the office names it applies to.
    /// </summary>
    public static readonly string[] CountyLevelOffices = ["County Court", "Associate County Court"];

    /// <summary>"State"/"Statewide" (case varies between the two files) marks a statewide office - not a real district.</summary>
    public static bool IsStatewideMarker(string district) =>
        string.Equals(district, "State", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(district, "Statewide", StringComparison.OrdinalIgnoreCase);
}
