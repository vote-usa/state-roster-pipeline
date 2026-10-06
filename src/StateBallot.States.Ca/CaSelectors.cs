using System.Text.Json;
using System.Text.RegularExpressions;

namespace StateBallot.States.Ca;

/// <summary>
/// Every CSS selector / regex used against sos.ca.gov pages and the certified
/// candidate list PDFs. Real collector runs load this from
/// data/input/ca/selectors.json (<see cref="Load"/>) so markup drift next year
/// only requires editing that file, not recompiling; <see cref="Default"/> holds
/// the same values compiled in - it's what selectors.json is seeded from, and
/// what tests use so they don't depend on file I/O.
/// </summary>
/// <remarks>
/// Deliberate exception to the rest of the codebase's mapper-class convention:
/// unlike TX/WV (a dedicated <c>*CandidateMapper</c> class) or WA (mapping
/// inlined once in the collector), CA's DTO-to-canonical-row construction for
/// each row type is inseparable from its own parser (PDF line regex vs.
/// AngleSharp DOM traversal vs. multi-paragraph content gathering), so it stays
/// as an <c>internal static ToXyzRow(...)</c> method inside each scraper/parser
/// file (<see cref="CertifiedListPdfParser"/>, <see cref="QualifiedMeasuresScraper"/>,
/// <see cref="UpcomingElectionsScraper"/>, <see cref="CountyDirectoryScraper"/>)
/// rather than being centralized into one <c>CaMapper</c> file. Grep for
/// "ToCandidateRow"/"ToMeasureRow"/"ToElection"/"ToCountyDirectoryRow" to find
/// the mapping step for each row type.
/// </remarks>
public sealed class CaSelectors
{
    // --- sos.ca.gov/elections/upcoming-elections ---
    /// <summary>Section headings ("Statewide Elections", "Special Vacancy Elections", ...).</summary>
    public required string UpcomingSectionHeading { get; init; }

    public required string StatewideSectionTitle { get; init; }
    public required string SpecialVacancySectionTitle { get; init; }

    /// <summary>Parses "General Election - November 3, 2026" / "Congressional District 14, Special General Election - August 18, 2026".</summary>
    public required Regex ElectionLinkText { get; init; }

    // --- sos.ca.gov qualified statewide ballot measures ---
    /// <summary>Election section heading, e.g. "November 3, 2026, Statewide Ballot Measures".</summary>
    public required Regex MeasuresElectionHeading { get; init; }

    /// <summary>Measure id heading, e.g. "Proposition 1".</summary>
    public required Regex PropositionHeading { get; init; }

    // --- sos.ca.gov county-administered elections + county elections offices ---
    /// <summary>Each county is an h2 whose text is the county name (usually wrapping a link to the county site).</summary>
    public required string CountySectionHeading { get; init; }

    public required string NoElectionsScheduledText { get; init; }

    /// <summary>Parses "August 25, 2026 – Special Election" (hyphen, en dash, or em dash).</summary>
    public required Regex CountyElectionLine { get; init; }

    /// <summary>
    /// Matches lines starting with a "(510) 272-6933" style phone number in
    /// county office blocks (some carry trailing text like ", option 1").
    /// </summary>
    public required Regex PhoneLine { get; init; }

    // --- special-election detail pages (e.g. /elections/upcoming-elections/2026-cd14) ---
    /// <summary>Date embedded in a round heading, e.g. "Special General Election, August 18, 2026".</summary>
    public required Regex SpecialElectionSectionDate { get; init; }

    public required Regex CertifiedListLinkText { get; init; }

    // --- certified list of candidates PDF ---
    /// <summary>Per-page header/footer lines to skip when parsing candidate pages.</summary>
    public required Regex CertListSkipLine { get; init; }

    /// <summary>Election title line on each PDF page, e.g. "Special General Election - August 18, 2026".</summary>
    public required Regex CertListElectionLine { get; init; }

    /// <summary>
    /// Candidate line: "Aisha Wahab* Democratic" / "Naomi Bar-Lev No Party Preference".
    /// The party is a qualified California party or "No Party Preference"; the
    /// optional "*" marks an incumbent. Everything before is the ballot name.
    /// </summary>
    public required Regex CertListCandidateLine { get; init; }

    /// <summary>Splits "United States Representative District 14" into office + district.</summary>
    public required Regex OfficeWithDistrict { get; init; }

    /// <summary>Regex keys that need RegexOptions.IgnoreCase when loaded from JSON.</summary>
    private static readonly HashSet<string> IgnoreCaseKeys = new(StringComparer.Ordinal) { "certifiedListLinkText" };

    /// <summary>Loads selector/pattern strings from data/input/&lt;xx&gt;/selectors.json, compiling regex patterns.</summary>
    public static CaSelectors Load(string path)
    {
        if (!File.Exists(path))
            throw new InvalidOperationException($"Selectors config file not found at {path}.");
        var raw = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path))
                  ?? throw new InvalidOperationException($"Selectors config at {path} is malformed (not a JSON object).");

        string Str(string key) => raw.TryGetValue(key, out var v)
            ? v : throw new InvalidOperationException($"Selectors config at {path} is missing key '{key}'.");
        Regex Rx(string key) => new(
            Str(key), (IgnoreCaseKeys.Contains(key) ? RegexOptions.IgnoreCase : RegexOptions.None) | RegexOptions.Compiled);

        return new CaSelectors
        {
            UpcomingSectionHeading = Str("upcomingSectionHeading"),
            StatewideSectionTitle = Str("statewideSectionTitle"),
            SpecialVacancySectionTitle = Str("specialVacancySectionTitle"),
            ElectionLinkText = Rx("electionLinkText"),
            MeasuresElectionHeading = Rx("measuresElectionHeading"),
            PropositionHeading = Rx("propositionHeading"),
            CountySectionHeading = Str("countySectionHeading"),
            NoElectionsScheduledText = Str("noElectionsScheduledText"),
            CountyElectionLine = Rx("countyElectionLine"),
            PhoneLine = Rx("phoneLine"),
            SpecialElectionSectionDate = Rx("specialElectionSectionDate"),
            CertifiedListLinkText = Rx("certifiedListLinkText"),
            CertListSkipLine = Rx("certListSkipLine"),
            CertListElectionLine = Rx("certListElectionLine"),
            CertListCandidateLine = Rx("certListCandidateLine"),
            OfficeWithDistrict = Rx("officeWithDistrict"),
        };
    }

    /// <summary>The compiled-in values; matches data/input/ca/selectors.json's seed content exactly.</summary>
    public static CaSelectors Default { get; } = new()
    {
        UpcomingSectionHeading = "h2, h3",
        StatewideSectionTitle = "Statewide Elections",
        SpecialVacancySectionTitle = "Special Vacancy Elections",
        ElectionLinkText =
            new(@"^(?<name>.+?)\s*[-–—]\s*(?<date>[A-Z][a-z]+ \d{1,2}, \d{4})\s*$", RegexOptions.Compiled),
        MeasuresElectionHeading =
            new(@"^(?<date>[A-Z][a-z]+ \d{1,2}, \d{4}),?\s+Statewide Ballot Measures", RegexOptions.Compiled),
        PropositionHeading = new(@"^Proposition\s+(?<num>\d+[A-Z]?)\s*$", RegexOptions.Compiled),
        CountySectionHeading = "h2",
        NoElectionsScheduledText = "No elections scheduled",
        CountyElectionLine =
            new(@"^(?<date>[A-Z][a-z]+ \d{1,2}, \d{4})\s*[-–—]\s*(?<name>.+?)\s*$", RegexOptions.Compiled),
        PhoneLine = new(@"^\(?\d{3}\)?[ .-]?\d{3}[ .-]?\d{4}", RegexOptions.Compiled),
        SpecialElectionSectionDate = new(@"(?<date>[A-Z][a-z]+ \d{1,2}, \d{4})", RegexOptions.Compiled),
        CertifiedListLinkText = new(@"Certified List of Candidates", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        CertListSkipLine = new(
            @"^(Official Certified List of Candidates|Page \d+ of \d+|\* ?Incumbent|\d{1,2}/\d{1,2}/\d{4})",
            RegexOptions.Compiled),
        CertListElectionLine =
            new(@"Election\s*[-–—]\s*[A-Z][a-z]+ \d{1,2}, \d{4}\s*$", RegexOptions.Compiled),
        CertListCandidateLine = new(
            @"^(?<name>.+?)(?<incumbent>\*)?\s+(?<party>Democratic|Republican|American Independent|Green|Libertarian|Peace and Freedom|No Party Preference|Unknown)\s*$",
            RegexOptions.Compiled),
        OfficeWithDistrict = new(@"^(?<office>.+?)\s+District\s+(?<district>\d+)\s*$", RegexOptions.Compiled),
    };
}
