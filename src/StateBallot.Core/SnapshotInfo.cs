namespace StateBallot.Core;

/// <summary>Pointer to the latest published snapshot in vote-usa/state-roster-data.</summary>
public sealed class SnapshotInfo
{
    public string Repository { get; set; } = DataPaths.DefaultDataRepoUrl;
    public string? Commit { get; set; }
    public string? GeneratedAt { get; set; }
    public int? Year { get; set; }
    public List<string> States { get; set; } = new();
    public string? Note { get; set; }
}
