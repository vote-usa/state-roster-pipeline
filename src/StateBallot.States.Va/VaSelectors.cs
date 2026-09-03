using System.Text.RegularExpressions;

namespace StateBallot.States.Va;

/// <summary>Every regex/CSS selector/office-name list used against VA DOE pages and XLSX cell values, centralized so drift only requires updating this file.</summary>
public static class VaSelectors
{
    /// <summary>
    /// Matches the index page's link text to the current cycle's general
    /// "All Offices" candidate list, e.g. "2026 November All Offices
    /// Candidate List" - loose enough to tolerate minor wording drift (only
    /// the leading year and trailing "All Offices Candidate List" are load-bearing).
    /// </summary>
    public static readonly Regex AllOfficesLinkText =
        new(@"^(?<year>\d{4}).*\bAll\s+Offices\s+Candidate\s+List$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>CSS selector for the linked candidate-list XLSX on an election page.</summary>
    public const string XlsxLinkCss = "a[href$='.xlsx']";

    /// <summary>
    /// Matches the election page's own &lt;title&gt; text, e.g. "Virginia Dept.
    /// of Elections: November 3, 2026 Gen Elect All Offices" - the only place
    /// the election's actual date is published; the XLSX itself never carries one.
    /// </summary>
    public static readonly Regex TitleDate =
        new(@"(?<date>[A-Za-z]+ \d{1,2}, \d{4})", RegexOptions.Compiled);

    /// <summary>Matches a District cell like "2nd District"/"10th District" down to its bare number.</summary>
    public static readonly Regex OrdinalDistrict =
        new(@"^(?<n>\d+)(?:st|nd|rd|th)\s+District$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>"Statewide" marks a statewide office - not a real district.</summary>
    public static bool IsStatewideMarker(string district) =>
        string.Equals(district, "Statewide", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Federal/state-legislative offices: statewide or district-wide, never
    /// attributed to one Locality even though the export repeats each one
    /// once per covered locality (see VaCandidateMapper.MergeDuplicateLocalities).
    /// Only "Member, United States Senate" and "Member, House of
    /// Representatives" are confirmed live in the 2026 general - Virginia's
    /// state legislature runs on an odd-year cycle, so neither chamber is on
    /// this (even-year) ballot to verify against. "Senate of Virginia"/"House
    /// of Delegates" are included from VA's publicly documented office-title
    /// convention but are NOT live-verified; confirm both against a real
    /// odd-year export before trusting them.
    /// </summary>
    public static readonly HashSet<string> WideOffices = new(StringComparer.OrdinalIgnoreCase)
    {
        "Member, United States Senate",
        "Member, House of Representatives",
        "Member, Senate of Virginia",
        "Member, House of Delegates",
    };
}
