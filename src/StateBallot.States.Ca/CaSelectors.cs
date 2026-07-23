using System.Text.RegularExpressions;

namespace StateBallot.States.Ca;

/// <summary>
/// Every CSS selector / regex used against sos.ca.gov pages and the certified
/// candidate list PDFs, centralized so markup drift next year only requires
/// updating this file.
/// </summary>
public static class CaSelectors
{
    // --- sos.ca.gov/elections/upcoming-elections ---
    /// <summary>Section headings ("Statewide Elections", "Special Vacancy Elections", ...).</summary>
    public const string UpcomingSectionHeading = "h2, h3";

    public const string StatewideSectionTitle = "Statewide Elections";
    public const string SpecialVacancySectionTitle = "Special Vacancy Elections";

    /// <summary>Parses "General Election - November 3, 2026" / "Congressional District 14, Special General Election - August 18, 2026".</summary>
    public static readonly Regex ElectionLinkText =
        new(@"^(?<name>.+?)\s*[-\u2013\u2014]\s*(?<date>[A-Z][a-z]+ \d{1,2}, \d{4})\s*$", RegexOptions.Compiled);

    // --- sos.ca.gov qualified statewide ballot measures ---
    /// <summary>Election section heading, e.g. "November 3, 2026, Statewide Ballot Measures".</summary>
    public static readonly Regex MeasuresElectionHeading =
        new(@"^(?<date>[A-Z][a-z]+ \d{1,2}, \d{4}),?\s+Statewide Ballot Measures", RegexOptions.Compiled);

    /// <summary>Measure id heading, e.g. "Proposition 1".</summary>
    public static readonly Regex PropositionHeading =
        new(@"^Proposition\s+(?<num>\d+[A-Z]?)\s*$", RegexOptions.Compiled);

    // --- sos.ca.gov county-administered elections + county elections offices ---
    /// <summary>Each county is an h2 whose text is the county name (usually wrapping a link to the county site).</summary>
    public const string CountySectionHeading = "h2";

    public const string NoElectionsScheduledText = "No elections scheduled";

    /// <summary>Parses "August 25, 2026 – Special Election" (hyphen, en dash, or em dash).</summary>
    public static readonly Regex CountyElectionLine =
        new(@"^(?<date>[A-Z][a-z]+ \d{1,2}, \d{4})\s*[-\u2013\u2014]\s*(?<name>.+?)\s*$", RegexOptions.Compiled);

    /// <summary>
    /// Matches lines starting with a "(510) 272-6933" style phone number in
    /// county office blocks (some carry trailing text like ", option 1").
    /// </summary>
    public static readonly Regex PhoneLine =
        new(@"^\(?\d{3}\)?[ .-]?\d{3}[ .-]?\d{4}", RegexOptions.Compiled);

    // --- certified list of candidates PDF ---
    /// <summary>Per-page header/footer lines to skip when parsing candidate pages.</summary>
    public static readonly Regex CertListSkipLine = new(
        @"^(Official Certified List of Candidates|Page \d+ of \d+|\* ?Incumbent|\d{1,2}/\d{1,2}/\d{4})",
        RegexOptions.Compiled);

    /// <summary>Election title line on each PDF page, e.g. "Special General Election - August 18, 2026".</summary>
    public static readonly Regex CertListElectionLine =
        new(@"Election\s*[-\u2013\u2014]\s*[A-Z][a-z]+ \d{1,2}, \d{4}\s*$", RegexOptions.Compiled);

    /// <summary>
    /// Candidate line: "Aisha Wahab* Democratic" / "Naomi Bar-Lev No Party Preference".
    /// The party is a qualified California party or "No Party Preference"; the
    /// optional "*" marks an incumbent. Everything before is the ballot name.
    /// </summary>
    public static readonly Regex CertListCandidateLine = new(
        @"^(?<name>.+?)(?<incumbent>\*)?\s+(?<party>Democratic|Republican|American Independent|Green|Libertarian|Peace and Freedom|No Party Preference|Unknown)\s*$",
        RegexOptions.Compiled);

    /// <summary>Splits "United States Representative District 14" into office + district.</summary>
    public static readonly Regex OfficeWithDistrict =
        new(@"^(?<office>.+?)\s+District\s+(?<district>\d+)\s*$", RegexOptions.Compiled);
}
