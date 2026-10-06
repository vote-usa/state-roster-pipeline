using System.Text.RegularExpressions;

namespace StateBallot.States.Nc;

/// <summary>Every regex used against NC candidate CSV cell values, centralized so format drift only requires updating this file.</summary>
public static class NcSelectors
{
    /// <summary>
    /// Splits a contest_name like "US HOUSE OF REPRESENTATIVES DISTRICT 09" or
    /// "NC SUPREME COURT ASSOCIATE JUSTICE SEAT 01" into office + district/seat
    /// number. The trailing token must contain a digit - "DISTRICT" is also part
    /// of some office titles that aren't numbered ("...SOIL AND WATER
    /// CONSERVATION DISTRICT SUPERVISOR"), which must NOT match here. Contests
    /// with no trailing "DISTRICT n"/"SEAT n" (e.g. "US SENATE", most
    /// county-level offices) don't match, and the raw contest_name is kept as
    /// Office unchanged - see CA's OfficeWithDistrict for the same convention.
    /// </summary>
    public static readonly Regex ContestWithDistrict =
        new(@"^(?<office>.+?)\s+(?:DISTRICT|SEAT)\s+(?<district>\d[\w-]*)$", RegexOptions.Compiled);
}
