namespace StateBallot.Core;

/// <summary>An upcoming election in the target state.</summary>
public sealed class Election
{
    /// <summary>Two-letter state code, e.g. "WA".</summary>
    public string State { get; set; } = "";

    /// <summary>The source system's identifier for the election (e.g. VoteWA election id).</summary>
    public string ElectionId { get; set; } = "";

    public string Name { get; set; } = "";
    public DateOnly ElectionDate { get; set; }
    public string ElectionType { get; set; } = "";
    public string Jurisdiction { get; set; } = "state";
    public string SourceUrl { get; set; } = "";
}

/// <summary>
/// Canonical candidate shape shared by every state. Fields a given state's source
/// doesn't publish are left null - never invented (see README conventions).
/// </summary>
public sealed class CandidateRow
{
    public string State { get; set; } = "";
    public string ElectionDate { get; set; } = "";
    public string ElectionType { get; set; } = "";
    public string Office { get; set; } = "";
    public string? District { get; set; }
    public string? County { get; set; }
    public string CandidateName { get; set; } = "";
    public string? Party { get; set; }
    public bool? Incumbent { get; set; }
    public string SourceUrl { get; set; } = "";

    /// <summary>The source system's identifier for the candidate, e.g. TX idCandidate.</summary>
    public string? SourceCandidateId { get; set; }
    public string? FilingDate { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? CampaignPhone { get; set; }
    public string? Website { get; set; }
    public string? Occupation { get; set; }
    public string? MailingAddressLine { get; set; }
    public string? MailingCity { get; set; }
    public string? MailingState { get; set; }
    public string? MailingZip { get; set; }
    public string? ResidentialCity { get; set; }
    public string? ResidentialCounty { get; set; }
}

public sealed class MeasureRow
{
    public string State { get; set; } = "";
    public string? ElectionDate { get; set; }
    public string MeasureId { get; set; } = "";
    public string Title { get; set; } = "";
    public string? Summary { get; set; }
    public string? FullTextUrl { get; set; }
    public string Jurisdiction { get; set; } = "";
    public string? County { get; set; }
    public string SourceUrl { get; set; } = "";
}

public sealed class CountyDirectoryRow
{
    public string State { get; set; } = "";
    public string CountyName { get; set; } = "";
    public string? CountyFips { get; set; }
    public string? ElectionsOfficeUrl { get; set; }
    public string? Address { get; set; }
    public string? Phone { get; set; }
}

/// <summary>One county's full ballot for one election.</summary>
public sealed class CountyBallot
{
    public string State { get; set; } = "";
    public string CountyName { get; set; } = "";
    public string ElectionDate { get; set; } = "";
    public string ElectionType { get; set; } = "";
    public List<CandidateRow> Candidates { get; set; } = new();
    public List<MeasureRow> Measures { get; set; } = new();
    public string SourceUrl { get; set; } = "";
}
