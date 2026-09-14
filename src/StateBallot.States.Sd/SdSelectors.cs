using System.Text.RegularExpressions;

namespace StateBallot.States.Sd;

/// <summary>Every regex/constant used against the SD VIP candidate portal and its export, centralized so drift only requires updating this file.</summary>
public static class SdSelectors
{
    /// <summary>The RadGrid toolbar button's full postback name (see WebFormsPostback.TriggerPostbackAsync).</summary>
    public const string ExportToCsvEventTarget = "ctl00$MainContent$grdCandidates$ctl00$ctl02$ctl00$ExportToCsvButton";

    /// <summary>Finds the election-calendar page's own link on the evergreen upcoming-elections page - its exact URL (year-folder name included) isn't assumed stable across cycles.</summary>
    public static readonly Regex CandidateCalendarLink =
        new(@"href=""(?<href>[^""]*candidate-calendar\.aspx)""", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Matches the calendar page's own "June 2, 2026 Primary Election" plain text (date first, then label - unlike every prior state's "Label: Date" shape).</summary>
    public static readonly Regex PrimaryElectionDateLine =
        new(@"(?<date>[A-Za-z]+ \d{1,2},\s*\d{4})\s+Primary Election", RegexOptions.Compiled);

    /// <summary>Matches the calendar page's own "November 3, 2026 General Election" plain text.</summary>
    public static readonly Regex GeneralElectionDateLine =
        new(@"(?<date>[A-Za-z]+ \d{1,2},\s*\d{4})\s+General Election", RegexOptions.Compiled);

    /// <summary>Matches "District 01" (State Senate/House) down to its bare number.</summary>
    public static readonly Regex NumberedDistrict =
        new(@"^District\s+0*(?<n>\d+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Matches a county's own sub-district shape - "{County}-{N}" (County
    /// Commissioner's most common form), "{County} - District {N}" (some
    /// counties spell it out, e.g. "Deuel - District 1"), or "{County} -
    /// Precinct-{N}"/"{County} - Precinct {N}" (Precinct Committeeman/woman,
    /// with its own inconsistent separator). The label word, if any, is
    /// generic (not hardcoded to "District"/"Precinct") since the shape is
    /// identical either way; the county part excludes hyphens entirely
    /// (<c>[^-]+?</c>) so it can never itself cross into a second hyphenated
    /// segment. A compound multi-district seat ("Lyman - District 1-2",
    /// covering sub-districts 1 and 2 as one seat) has no single bare number
    /// to extract and deliberately fails to match here - see
    /// SdCandidateMapper's fallback for that case. A genuine hyphenated
    /// county-pair name with no trailing number at all, like "Brule-Buffalo"
    /// (Conservation District Supervisor), also never matches, since the
    /// pattern requires the value to *end* in one.
    /// </summary>
    public static readonly Regex CountySubDistrict =
        new(@"^(?<county>[^-]+?)\s*-\s*(?:\w+[\s-]*)?0*(?<n>\d+)$", RegexOptions.Compiled);

    /// <summary>Strips a trailing " County" some (not all) rows include, for consistent output regardless of which office's row it came from.</summary>
    public static readonly Regex CountySuffix =
        new(@"\s+County$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Matches a "Mailing Address" cell's trailing "ST 12345" or "ST 12345-6789" - the only reliably-separable part; street and city run together with no delimiter at all and aren't split further.</summary>
    public static readonly Regex TrailingStateZip =
        new(@"^(?<rest>.+?)\s+(?<state>[A-Z]{2})\s+(?<zip>\d{5}(-\d{4})?)$", RegexOptions.Compiled);

    /// <summary>
    /// Offices whose "District/County" column is the office's own real
    /// county jurisdiction (a bare county name, "{County}-{N}" for County
    /// Commissioner's own sub-districts, or occasionally "{County} County") -
    /// everything else non-blank and not a numbered legislative district
    /// (Alderman, City Commissioner, water development district directors,
    /// ...) is a real value but not a county one (a city/ward/special-
    /// district name), so it's kept verbatim in District rather than guessed
    /// at - see SdCandidateMapper.
    /// </summary>
    public static readonly HashSet<string> CountyOffices = new(StringComparer.OrdinalIgnoreCase)
    {
        "Sheriff",
        "County Auditor",
        "States Attorney",
        "Coroner",
        "Register of Deeds",
        "County Treasurer",
        "County Finance Officer",
        "County Commissioner",
        "County Commissioner At Large",
        "Conservation District Supervisor",
        // Party-organizational roles, not government offices, but tied to a
        // real county (and, for the two Precinct roles, a sub-county
        // precinct) the same way County Commissioner is tied to a
        // sub-district - same "{County} - {label} {n}" shape, so handled
        // identically rather than as a special case.
        "Delegates to State Convention",
        "Precinct Committeeman",
        "Precinct Committeewoman",
    };
}
