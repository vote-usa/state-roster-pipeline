using System.Text.RegularExpressions;

namespace StateBallot.States.Wy;

/// <summary>Every regex used against WY SOS pages and CSV cell values, centralized so drift only requires updating this file.</summary>
public static class WySelectors
{
    // --- elections info page's embedded schema.org JSON-LD ---
    /// <summary>Matches a "20NN Wyoming Primary Election" / "20NN Wyoming General Election" JSON-LD Event name.</summary>
    public static readonly Regex ElectionEventName =
        new(@"^\d{4}\s+Wyoming\s+(?<type>Primary|General)\s+Election$", RegexOptions.Compiled);

    // --- candidate CSV "Office Sought" column ---
    /// <summary>
    /// Primary-only: strips a trailing " - REPUBLICAN"/" - DEMOCRATIC" suffix
    /// (redundant with the CSV's own Party Affiliation column; General's Office
    /// Sought never carries this suffix).
    /// </summary>
    public static readonly Regex PartySuffix =
        new(@"^(?<office>.+?)\s*-\s*(?:REPUBLICAN|DEMOCRATIC)\s*$", RegexOptions.Compiled);

    /// <summary>
    /// Judicial races are coded "CC-01 - FIRST JUDICIAL DISTRICT, CIRCUIT COURT JUDGE" -
    /// the code (Circuit/District/Chancery/Supreme Court seat id) becomes District,
    /// the descriptive text becomes Office.
    /// </summary>
    public static readonly Regex JudicialCode =
        new(@"^(?<code>[A-Z]{2,4}-\d+)\s*-\s*(?<office>.+)$", RegexOptions.Compiled);

    /// <summary>Legislative races are a bare trailing number, e.g. "STATE SENATOR 01" (no "DISTRICT"/"SEAT" keyword, unlike NC/CA).</summary>
    public static readonly Regex TrailingDistrictNumber =
        new(@"^(?<office>.+?)\s+(?<district>\d+)\s*$", RegexOptions.Compiled);

    // --- candidate CSV "Mailing City State & Zip" column ---
    public static readonly Regex MailingCityStateZip =
        new(@"^(?<city>.+?)\s+(?<state>[A-Z]{2})\s+(?<zip>\d{5}(-\d{4})?)$", RegexOptions.Compiled);
}
