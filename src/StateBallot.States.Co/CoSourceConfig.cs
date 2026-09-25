namespace StateBallot.States.Co;

/// <summary>All source URLs for the CO SOS elections site in one place.</summary>
public sealed class CoSourceConfig
{
    public string BaseUrl { get; init; } = "https://www.sos.state.co.us";

    /// <summary>
    /// Candidate list landing page for one election type. Neither page is
    /// year-parameterized in its URL - it always reflects whatever the current
    /// cycle is - so the collector cross-checks the page's own "20NN &lt;type&gt;
    /// Election ... Candidate List" heading against the requested year rather
    /// than trusting the URL alone (see CoCollector).
    /// </summary>
    /// <param name="kind">"Primary" or "General" (case-insensitive).</param>
    public string CandidateListPageUrl(string kind) =>
        string.Equals(kind, "Primary", StringComparison.OrdinalIgnoreCase)
            ? $"{BaseUrl}/pubs/elections/vote/primaryCandidates.html"
            : $"{BaseUrl}/pubs/elections/vote/generalCandidates.html";

    /// <summary>
    /// Statutory election calendar PDF. Its first page carries both elections'
    /// actual dates ("Primary Election: June 30, 2026" / "General Election:
    /// November 3, 2026") - the only place either date is published; the
    /// candidate list pages/XLSX never carry a date, only a text label.
    /// </summary>
    public string ElectionCalendarPdfUrl(int year) =>
        $"{BaseUrl}/pubs/elections/calendars/{year}ElectionCalendar.pdf";

    /// <summary>Resolves an href from a candidate list page (e.g. a relative "files/2026/...xlsx") to an absolute URL.</summary>
    public string Resolve(string baseUrl, string href) => new Uri(new Uri(baseUrl), href).ToString();
}
