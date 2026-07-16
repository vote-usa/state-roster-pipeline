namespace StateBallot.Core;

/// <summary>Everything a state collector produces for one run.</summary>
public sealed class CollectResult
{
    public List<Election> Elections { get; init; } = new();

    /// <summary>Source-system county code => county name (e.g. VoteWA "01" => "Adams").</summary>
    public SortedDictionary<string, string> CountyCodes { get; init; } = new();

    public List<CandidateRow> Candidates { get; init; } = new();

    /// <summary>Measures already certified to a ballot (typically local/county).</summary>
    public List<MeasureRow> Measures { get; init; } = new();

    /// <summary>Statewide measures collected from the state's measure-information source.</summary>
    public List<MeasureRow> StatewideProposedMeasures { get; init; } = new();

    public List<CountyDirectoryRow> CountyDirectory { get; init; } = new();
    public List<CountyBallot> CountyBallots { get; init; } = new();
    public List<string> Gaps { get; init; } = new();

    /// <summary>Elections whose ballot data is not yet published by the source.</summary>
    public List<Election> PendingElections { get; init; } = new();

    /// <summary>
    /// Provenance manifest entries for sources.json: data group name => URL/format
    /// descriptors. Populated by the state collector.
    /// </summary>
    public Dictionary<string, object?> SourceGroups { get; init; } = new();

    /// <summary>Machine-readable re-run recommendation for sources.json.</summary>
    public object? NextRun { get; set; }

    public void PrintSummary(TextWriter writer)
    {
        writer.WriteLine();
        writer.WriteLine("Summary:");
        writer.WriteLine($"  Elections covered:            {Elections.Count}");
        foreach (var e in Elections)
            writer.WriteLine($"    - {e.Name} ({e.ElectionDate:yyyy-MM-dd}, {e.ElectionType})");
        writer.WriteLine($"  Candidates (all levels):      {Candidates.Count}");
        writer.WriteLine($"    statewide/district (no county): {Candidates.Count(c => c.County is null)}");
        writer.WriteLine($"    county/local:                   {Candidates.Count(c => c.County is not null)}");
        writer.WriteLine($"  Local measures on ballots:    {Measures.Count}");
        writer.WriteLine($"  Statewide proposed measures:  {StatewideProposedMeasures.Count}");
        writer.WriteLine($"  Counties in directory:        {CountyDirectory.Count}");
        writer.WriteLine($"  County ballots collected:     {CountyBallots.Count}");
        if (Gaps.Count > 0)
        {
            writer.WriteLine("  Gaps:");
            foreach (var gap in Gaps)
                writer.WriteLine($"    ! {gap}");
        }
    }
}
