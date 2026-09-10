using System.Text.Json.Serialization;

namespace StateBallot.Core.Publishing;

/// <summary>run.json: provenance for one election directory produced by one collector run.</summary>
public sealed class RunMetadata
{
    public string State { get; set; } = "";
    public string ElectionDate { get; set; } = "";
    public int Year { get; set; }
    public string GeneratedAt { get; set; } = "";
    public string? PipelineCommit { get; set; }
    public string PipelineVersion { get; set; } = "";
    public string? Wayback { get; set; }
    public string? CliArgs { get; set; }
    public string RequestedBy { get; set; } = "";

    /// <summary>Source election ids on this date, in elections.json order.</summary>
    public List<string> ElectionIds { get; set; } = new();

    /// <summary>True when every election on this date is still unpublished at the source.</summary>
    public bool Pending { get; set; }

    public RunCounts Counts { get; set; } = new();

    /// <summary>Raw source election type -> published vocabulary, for the elections on this date.</summary>
    public SortedDictionary<string, string> ElectionTypesRaw { get; set; } = new(StringComparer.Ordinal);

    /// <summary>Every gap the run reported, not only this date's.</summary>
    public List<string> Gaps { get; set; } = new();

    public NextRunOut? NextRun { get; set; }

    /// <summary>Provenance per data group (url + format), as the collector reported it.</summary>
    [JsonExtensionData]
    public Dictionary<string, object?> Extra { get; set; } = new();
}

public sealed class RunCounts
{
    public int Elections { get; set; }
    public int Candidates { get; set; }
    public int Measures { get; set; }
    public int CountyBallots { get; set; }
}

public sealed class NextRunOut
{
    public string RecommendedAfter { get; set; } = "";
    public string Reason { get; set; } = "";
    public string? NextElectionDate { get; set; }
    public string? NextElectionType { get; set; }
}
