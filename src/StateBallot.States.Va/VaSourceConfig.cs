namespace StateBallot.States.Va;

/// <summary>All source URLs for the VA Dept. of Elections site in one place.</summary>
public sealed class VaSourceConfig
{
    public string BaseUrl { get; init; } = "https://www.elections.virginia.gov";

    /// <summary>
    /// Evergreen index of the current cycle's candidate-list pages. Individual
    /// election pages (and the XLSX files they link to) have no consistent,
    /// year-templatable naming at all - hand-typed slugs, revision-dated
    /// filenames, primaries split per-party into separate files - so this is
    /// discovered by scraping the index for the current cycle's "All Offices"
    /// link rather than templated (see VaCollector).
    /// </summary>
    public string CandidateListIndexUrl => $"{BaseUrl}/casting-a-ballot/candidate-list/";
}
