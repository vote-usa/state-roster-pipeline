using System.Text.RegularExpressions;

namespace StateBallot.States.Ne;

/// <summary>Every regex used against NE SOS pages and workbook cell values, centralized so drift only requires updating this file.</summary>
public static class NeSelectors
{
    // --- sos.nebraska.gov/elections page text ---
    public static readonly Regex PrimaryDate =
        new(@"Primary Election:\s*(?<date>[A-Za-z]+\s+\d{1,2},\s*\d{4})", RegexOptions.Compiled);

    public static readonly Regex GeneralDate =
        new(@"General Election:\s*(?<date>[A-Za-z]+\s+\d{1,2},\s*\d{4})", RegexOptions.Compiled);

    // --- Statewide_Candidate_Filing_List.xlsx sheet 0 "Office" column ---
    /// <summary>
    /// Statewide/federal/legislative offices are labeled "For United States
    /// Senator", "For Member of the Legislature", etc. - stripped down to the
    /// bare office name. Local special-district entities (public power
    /// districts, natural resources districts, community colleges, ...) don't
    /// start with "For " at all (their own proper name comes first, sometimes
    /// with "For Board of ..." in the middle), so this anchored match leaves
    /// them untouched rather than needing a separate code path.
    /// </summary>
    public static readonly Regex OfficeForPrefix = new(@"^For\s+", RegexOptions.Compiled);

    // --- Statewide_Candidate_Filing_List.xlsx sheet 0 "Mailing Address" last line ---
    public static readonly Regex CityStateZip =
        new(@"^(?<city>.+?)\s+(?<state>[A-Z]{2})\s+(?<zip>\d{5}(-\d{4})?)$", RegexOptions.Compiled);
}
