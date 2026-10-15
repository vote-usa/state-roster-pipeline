using StateBallot.Core;

namespace StateBallot.States.Co;

/// <summary>
/// Named accessors over Colorado's source links (data/input/co/source_links.json,
/// or the SourceLinks table when a run has a database). The URLs themselves are data.
/// </summary>
public sealed class CoSourceConfig : SourceConfigBase
{
    /// <summary>The candidate list page, the same URL every cycle; it links the year's XLSX.</summary>
    /// <param name="kind">"Primary" or "General" (case-insensitive); anything else is treated as General.</param>
    public string CandidateListPageUrl(string kind) => Links.Url("candidate-list-page", PrimaryOrGeneral(kind));

    public string ElectionCalendarPdfUrl(int year) => Links.Url("election-calendar", year: year);

    public string Resolve(string baseUrl, string href) => new Uri(new Uri(baseUrl), href).ToString();
}
