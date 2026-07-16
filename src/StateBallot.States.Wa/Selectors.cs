using System.Text.RegularExpressions;

namespace StateBallot.States.Wa;

/// <summary>
/// Every CSS selector / regex used against source pages, centralized so markup
/// drift next year only requires updating this file.
/// </summary>
public static class Selectors
{
    // --- voter.votewa.gov/CandidateList.aspx (WebForms HTML) ---
    public const string ElectionDropdown = "select#ddlElection, select[name$='ddlElection']";
    public const string CountyDropdown = "select#ddlCounty, select[name$='ddlCounty']";

    /// <summary>Parses "PRIMARY 2026 (08/04/2026) (Primary)" into name, date, type.</summary>
    public static readonly Regex ElectionOptionText =
        new(@"^(?<name>.+?)\s*\((?<date>\d{2}/\d{2}/\d{4})\)\s*\((?<type>[^)]+)\)\s*$", RegexOptions.Compiled);

    // --- voterguide.ashx JSON (party strings) ---
    /// <summary>Extracts "Democratic Party" from "(Prefers Democratic Party)".</summary>
    public static readonly Regex PartyPreference =
        new(@"^\(?\s*(?:States No Party Preference|Prefers\s+(?<party>.+?))\s*\)?$", RegexOptions.Compiled);

    // --- sos.wa.gov statewide measures page (Drupal HTML) ---
    /// <summary>Year section headings, e.g. an h2 containing "2026".</summary>
    public const string MeasuresYearHeading = "h2";

    /// <summary>Measure headings, e.g. "Initiative No. IL26-001" / "Initiative Measure No. 2124".</summary>
    public const string MeasureHeading = "h3";

    public static readonly Regex MeasureHeadingText =
        new(@"(?<kind>Initiative|Referendum|Senate Joint Resolution|House Joint Resolution|Engrossed.+?Resolution)\s*(?:Measure\s*)?(?:No\.?\s*)?(?<id>[A-Z]{0,4}\d[\w-]*)", RegexOptions.Compiled);

    public const string MeasurePdfLink = "a[href$='.pdf' i]";
    public static readonly Regex FullTextLink = new(@"full\s*text", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // --- sos.wa.gov county elections offices page (Drupal HTML) ---
    /// <summary>Rows of the office grid table; tolerate either the table class or any table under #officegrid.</summary>
    public const string CountyOfficeRows = "#officegrid table tbody tr, table.cols-4 tbody tr";

    public const string CountyOfficeCountyCell = "td[headers*='county'], td.views-field-field-county";
    public const string CountyOfficeAddressCell = "td[headers*='address'], td.views-field-field-address";
    public const string CountyOfficeContactCell = "td[headers*='nothing'], td.views-field-nothing";
    public const string CountyOfficeWebsiteLink = "a[href^='http']";
    public static readonly Regex PhoneLine = new(@"P:\s*(?<phone>[\d()\- .]{7,})", RegexOptions.Compiled);
}
