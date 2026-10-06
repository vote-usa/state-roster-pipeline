using System.Text.RegularExpressions;

namespace StateBallot.States.Md;

/// <summary>
/// Every selector/regex used against MD SBE pages and CSV cell values,
/// centralized so markup/format drift next year only requires updating this
/// file.
/// </summary>
public static class MdSelectors
{
    // --- elections.maryland.gov/elections/{year}/index.html ---
    /// <summary>Matches a "Primary Election Day" / "General Election Day" &lt;dt&gt; label.</summary>
    public static readonly Regex ElectionDayLabel =
        new(@"^(?<type>Primary|General)\s+Election\s+Day$", RegexOptions.Compiled);

    // --- statewide candidate list CSV ---
    /// <summary>Splits "Campaign Mailing City State and Zip" (e.g. "Bethesda MD 20814") into parts.</summary>
    public static readonly Regex MailingCityStateZip =
        new(@"^(?<city>.+?)\s+(?<state>[A-Z]{2})\s+(?<zip>\d{5}(-\d{4})?)$", RegexOptions.Compiled);
}
