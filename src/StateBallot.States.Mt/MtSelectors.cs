using System.Text.RegularExpressions;

namespace StateBallot.States.Mt;

/// <summary>Every regex/constant used against the MT candidate filing portal and its export, centralized so drift only requires updating this file.</summary>
public static class MtSelectors
{
    /// <summary>The RadGrid toolbar button's full postback name (see WebFormsPostback.TriggerPostbackAsync - same doPostBack shape as SD's).</summary>
    public const string ExportToCsvEventTarget = "ctl00$ContentPlaceHolder1$grdCandidates$ctl00$ctl02$ctl00$ExportToCsvButton";

    /// <summary>The "ddlElection" &lt;select&gt;'s own markup block, isolated so its &lt;option&gt;s aren't confused with any other dropdown on the page.</summary>
    public static readonly Regex ElectionDropdownBlock =
        new(@"<select[^>]*id=""ddlElection""[^>]*>(?<options>.*?)</select>", RegexOptions.Singleline | RegexOptions.Compiled);

    /// <summary>One &lt;option value="..."&gt;text&lt;/option&gt; inside the dropdown block above.</summary>
    public static readonly Regex OptionTag =
        new(@"<option[^>]*value=""(?<value>[^""]*)""[^>]*>(?<text>[^<]*)</option>", RegexOptions.Compiled);

    /// <summary>Matches one option's own text, e.g. "FEDERAL PRIMARY 2026 (06/02/2026) (Primary)".</summary>
    public static readonly Regex ElectionOptionText =
        new(@"^(?<name>.+?)\s*\((?<date>\d{1,2}/\d{1,2}/\d{4})\)\s*\((?<type>Primary|General)\)$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// "STATE REPRESENTATIVE DISTRICT 1", "STATE SENATOR DISTRICT 10",
    /// "PUBLIC SERVICE COMMISSIONER, DISTRICT 1" (comma optional) - the most
    /// common shape, used for every district type except the three below.
    /// </summary>
    public static readonly Regex TrailingDistrictNumber =
        new(@"^(?<office>.+?),?\s+DISTRICT\s+(?<n>\d+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// "DISTRICT COURT JUDGE DISTRICT 20, DEPT 2 UNEXPIRED" - the department
    /// (and any "UNEXPIRED"-term flag) stays folded into District rather than
    /// discarded, since more than one judge seat can exist within the same
    /// numbered judicial district.
    /// </summary>
    public static readonly Regex JudicialDistrict =
        new(@"^(?<office>.+?)\s+DISTRICT\s+(?<rest>\d+.*)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>"SUPREME COURT JUSTICE #4" - a numbered seat, not a geographic district at all.</summary>
    public static readonly Regex NumberedSeat =
        new(@"^(?<office>.+?)\s*#(?<n>\d+)$", RegexOptions.Compiled);

    /// <summary>
    /// "1ST CONGRESSIONAL"/"2ND CONGRESSIONAL" (the "District" column) - the
    /// only district type whose Race column never embeds its own number at
    /// all ("UNITED STATES REPRESENTATIVE" alone, for every district).
    /// </summary>
    public static readonly Regex OrdinalCongressionalDistrict =
        new(@"^(?<n>\d+)(?:ST|ND|RD|TH)\s+CONGRESSIONAL$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>A comma-joined "street[, city][, state][, zip]" cell, read right-to-left since any of city/state can be omitted (confirmed live) but never out of this order.</summary>
    public static readonly Regex TwoLetterState = new(@"^[A-Z]{2}$", RegexOptions.Compiled);
    public static readonly Regex LeadingZipDigits = new(@"^\d{5}", RegexOptions.Compiled);
}
