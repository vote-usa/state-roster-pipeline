using System.Text.Json;
using System.Text.RegularExpressions;

namespace StateBallot.States.Wa;

/// <summary>
/// Every CSS selector / regex used against source pages. Real collector runs
/// load this from data/input/wa/selectors.json (<see cref="Load"/>) so markup
/// drift next year only requires editing that file, not recompiling;
/// <see cref="Default"/> holds the same values compiled in - it's what
/// selectors.json is seeded from, and what tests use so they don't depend on
/// file I/O.
/// </summary>
public sealed class Selectors
{
    // --- voter.votewa.gov/CandidateList.aspx (WebForms HTML) ---
    public required string ElectionDropdown { get; init; }
    public required string CountyDropdown { get; init; }

    /// <summary>Parses "PRIMARY 2026 (08/04/2026) (Primary)" into name, date, type.</summary>
    public required Regex ElectionOptionText { get; init; }

    // --- voterguide.ashx JSON (party strings) ---
    /// <summary>Extracts "Democratic Party" from "(Prefers Democratic Party)".</summary>
    public required Regex PartyPreference { get; init; }

    // --- sos.wa.gov statewide measures page (Drupal HTML) ---
    /// <summary>Year section headings, e.g. an h2 containing "2026".</summary>
    public required string MeasuresYearHeading { get; init; }

    /// <summary>Measure headings, e.g. "Initiative No. IL26-001" / "Initiative Measure No. 2124".</summary>
    public required string MeasureHeading { get; init; }

    public required Regex MeasureHeadingText { get; init; }

    public required string MeasurePdfLink { get; init; }
    public required Regex FullTextLink { get; init; }

    // --- sos.wa.gov county elections offices page (Drupal HTML) ---
    /// <summary>Rows of the office grid table; tolerate either the table class or any table under #officegrid.</summary>
    public required string CountyOfficeRows { get; init; }

    public required string CountyOfficeCountyCell { get; init; }
    public required string CountyOfficeAddressCell { get; init; }
    public required string CountyOfficeContactCell { get; init; }
    public required string CountyOfficeWebsiteLink { get; init; }
    public required Regex PhoneLine { get; init; }

    /// <summary>Regex keys that need RegexOptions.IgnoreCase when loaded from JSON.</summary>
    private static readonly HashSet<string> IgnoreCaseKeys = new(StringComparer.Ordinal) { "fullTextLink" };

    /// <summary>Loads selector/pattern strings from data/input/wa/selectors.json, compiling regex patterns.</summary>
    public static Selectors Load(string path)
    {
        if (!File.Exists(path))
            throw new InvalidOperationException($"Selectors config file not found at {path}.");
        var raw = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path))
                  ?? throw new InvalidOperationException($"Selectors config at {path} is malformed (not a JSON object).");

        string Str(string key) => raw.TryGetValue(key, out var v)
            ? v : throw new InvalidOperationException($"Selectors config at {path} is missing key '{key}'.");
        Regex Rx(string key) => new(
            Str(key), (IgnoreCaseKeys.Contains(key) ? RegexOptions.IgnoreCase : RegexOptions.None) | RegexOptions.Compiled);

        return new Selectors
        {
            ElectionDropdown = Str("electionDropdown"),
            CountyDropdown = Str("countyDropdown"),
            ElectionOptionText = Rx("electionOptionText"),
            PartyPreference = Rx("partyPreference"),
            MeasuresYearHeading = Str("measuresYearHeading"),
            MeasureHeading = Str("measureHeading"),
            MeasureHeadingText = Rx("measureHeadingText"),
            MeasurePdfLink = Str("measurePdfLink"),
            FullTextLink = Rx("fullTextLink"),
            CountyOfficeRows = Str("countyOfficeRows"),
            CountyOfficeCountyCell = Str("countyOfficeCountyCell"),
            CountyOfficeAddressCell = Str("countyOfficeAddressCell"),
            CountyOfficeContactCell = Str("countyOfficeContactCell"),
            CountyOfficeWebsiteLink = Str("countyOfficeWebsiteLink"),
            PhoneLine = Rx("phoneLine"),
        };
    }

    /// <summary>The compiled-in values; matches data/input/wa/selectors.json's seed content exactly.</summary>
    public static Selectors Default { get; } = new()
    {
        ElectionDropdown = "select#ddlElection, select[name$='ddlElection']",
        CountyDropdown = "select#ddlCounty, select[name$='ddlCounty']",
        ElectionOptionText =
            new(@"^(?<name>.+?)\s*\((?<date>\d{2}/\d{2}/\d{4})\)\s*\((?<type>[^)]+)\)\s*$", RegexOptions.Compiled),
        PartyPreference =
            new(@"^\(?\s*(?:States No Party Preference|Prefers\s+(?<party>.+?))\s*\)?$", RegexOptions.Compiled),
        MeasuresYearHeading = "h2",
        MeasureHeading = "h3",
        MeasureHeadingText = new(
            @"(?<kind>Initiative|Referendum|Senate Joint Resolution|House Joint Resolution|Engrossed.+?Resolution)\s*(?:Measure\s*)?(?:No\.?\s*)?(?<id>[A-Z]{0,4}\d[\w-]*)",
            RegexOptions.Compiled),
        MeasurePdfLink = "a[href$='.pdf' i]",
        FullTextLink = new(@"full\s*text", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        CountyOfficeRows = "#officegrid table tbody tr, table.cols-4 tbody tr",
        CountyOfficeCountyCell = "td[headers*='county'], td.views-field-field-county",
        CountyOfficeAddressCell = "td[headers*='address'], td.views-field-field-address",
        CountyOfficeContactCell = "td[headers*='nothing'], td.views-field-nothing",
        CountyOfficeWebsiteLink = "a[href^='http']",
        PhoneLine = new(@"P:\s*(?<phone>[\d()\- .]{7,})", RegexOptions.Compiled),
    };
}
