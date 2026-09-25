using System.Text.RegularExpressions;

namespace StateBallot.States.Nm;

/// <summary>Every regex/constant used against the NM candidate portal and its export, centralized so drift only requires updating this file.</summary>
public static class NmSelectors
{
    // --- WebForms postback field names (ctl00$MainContent$... prefix, per ASP.NET's naming convention for a control nested this deeply) ---
    public const string ExportFormatField = "ctl00$MainContent$ddlExportFormat";
    public const string PartyFilterField = "ctl00$MainContent$ddlParty";
    public const string CountyFilterField = "ctl00$MainContent$ddlCounty";
    public const string ExportButtonField = "ctl00$MainContent$btnExport";

    /// <summary>
    /// The dropdown's own option value for "Excel (xls)" - actually a plain
    /// HTML &lt;table&gt; wearing a misleading extension/content-type (a
    /// common Telerik RadGrid trick), not a real spreadsheet. Deliberately
    /// preferred over the "Text File (csv)" option: the CSV export has a real
    /// data-quality bug where some fields (name suffixes, street addresses)
    /// contain unescaped commas that silently shift every later column on
    /// that row - confirmed live, ~13% of rows affected. HTML cells have no
    /// such ambiguity (see Core's HtmlTableParser).
    /// </summary>
    public const string ExportFormatValue = "Excel (xls)";

    /// <summary>Dropdown value meaning "every party" / "every county" (both use "0" for their blank/All option).</summary>
    public const string AllPartiesOrCounties = "0";

    /// <summary>Matches the upcoming-elections page's "20NN General Election: Tuesday, November 3, 2026" plain-text line.</summary>
    public static readonly Regex GeneralElectionDateLine =
        new(@"General\s+Election:\s*(?:\w+,\s*)?(?<date>[A-Za-z]+ \d{1,2}, \d{4})", RegexOptions.Compiled);

    /// <summary>Matches a bare numbered district cell ("DISTRICT 33", "COUNTY COMMISSION DISTRICT 1") down to its number.</summary>
    public static readonly Regex NumberedDistrict =
        new(@"^(?:COUNTY\s+COMMISSION\s+)?DIST(?:RICT)?\s+(?<n>\d+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Offices whose "Filing County" column is the office's own real,
    /// whole-or-sub-divided-county jurisdiction - everything else's Filing
    /// County is just where that candidate happens to have filed, not the
    /// race's true scope (a legislative/congressional district crosses county
    /// lines; a judicial district or municipal court division isn't a county
    /// at all) - see NmCandidateMapper.
    /// </summary>
    public static readonly HashSet<string> CountyScopedOffices = new(StringComparer.OrdinalIgnoreCase)
    {
        "County Assessor",
        "County Clerk",
        "County Commissioner At Large",
        "County Commissioner by Commissioner District",
        "County Sheriff",
        "County Treasurer",
        "Probate Judge",
        "Magistrate Judge",
        "Judge of the Metropolitan Court",
    };

    /// <summary>Matches a Contest cell beginning with "Judicial Retention" - a yes/no retention vote, not a multi-candidate race (see NmRetentionMapper).</summary>
    public static readonly Regex JudicialRetentionPrefix =
        new(@"^Judicial\s+Retention\s*", RegexOptions.IgnoreCase | RegexOptions.Compiled);
}
