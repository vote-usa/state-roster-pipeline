namespace StateBallot.Core.Output;

/// <summary>Stable cross-state output row for elections.json|csv.</summary>
public sealed class ElectionOut
{
    public string State { get; set; } = "";
    public string ElectionDate { get; set; } = "";
    public string ElectionType { get; set; } = "";
    public string Jurisdiction { get; set; } = "";
    public string? OcdDivisionId { get; set; }
    public string Name { get; set; } = "";
    public string ElectionId { get; set; } = "";
    public string SourceUrl { get; set; } = "";
}

/// <summary>Stable cross-state output row for candidates.json|csv.</summary>
public sealed class CandidateOut
{
    public string State { get; set; } = "";
    public string ElectionDate { get; set; } = "";
    public string ElectionType { get; set; } = "";
    public string Office { get; set; } = "";
    public string? District { get; set; }
    public string? County { get; set; }
    public string? OcdDivisionId { get; set; }
    public string CandidateName { get; set; } = "";
    public string? Party { get; set; }
    public bool? Incumbent { get; set; }
    public string SourceUrl { get; set; } = "";
}

/// <summary>Stable cross-state output row for measures.json|csv.</summary>
public sealed class MeasureOut
{
    public string State { get; set; } = "";
    public string? ElectionDate { get; set; }
    public string MeasureId { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Summary { get; set; }
    public string? FullTextUrl { get; set; }
    public string Jurisdiction { get; set; } = "";
    public string? County { get; set; }
    public string? OcdDivisionId { get; set; }
    public string SourceUrl { get; set; } = "";
}

/// <summary>Stable cross-state output row for county_directory.json.</summary>
public sealed class CountyDirectoryOut
{
    public string State { get; set; } = "";
    public string CountyName { get; set; } = "";
    public string? CountyFips { get; set; }
    public string? OcdDivisionId { get; set; }
    public string? ElectionsOfficeUrl { get; set; }
    public string? Address { get; set; }
    public string? Phone { get; set; }
}

/// <summary>One candidate entry embedded in a county ballot JSON object.</summary>
public sealed class CountyBallotCandidateOut
{
    public string Office { get; set; } = "";
    public string? District { get; set; }
    public string? OcdDivisionId { get; set; }
    public string CandidateName { get; set; } = "";
    public string? Party { get; set; }
}

/// <summary>One measure entry embedded in a county ballot JSON object.</summary>
public sealed class CountyBallotMeasureOut
{
    public string MeasureId { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Summary { get; set; }
    public string Jurisdiction { get; set; } = "";
    public string? OcdDivisionId { get; set; }
}

/// <summary>Stable cross-state output object for county_ballots.json.</summary>
public sealed class CountyBallotOut
{
    public string State { get; set; } = "";
    public string County { get; set; } = "";
    public string? OcdDivisionId { get; set; }
    public string ElectionDate { get; set; } = "";
    public string ElectionType { get; set; } = "";
    public List<CountyBallotCandidateOut> Candidates { get; set; } = new();
    public List<CountyBallotMeasureOut> Measures { get; set; } = new();
    public string SourceUrl { get; set; } = "";
}

/// <summary>Flat CSV row for county_ballots.csv (one candidate or measure per row).</summary>
public sealed class CountyBallotCsvOut
{
    public string State { get; set; } = "";
    public string County { get; set; } = "";
    public string? OcdDivisionId { get; set; }
    public string ElectionDate { get; set; } = "";
    public string ElectionType { get; set; } = "";
    public string EntryType { get; set; } = "";
    public string? Office { get; set; }
    public string? District { get; set; }
    public string? CandidateName { get; set; }
    public string? Party { get; set; }
    public string? MeasureId { get; set; }
    public string? Title { get; set; }
    public string SourceUrl { get; set; } = "";
}
