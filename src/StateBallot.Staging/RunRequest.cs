namespace StateBallot.Staging;

/// <summary>Everything needed to execute one collection pass, from the CLI or the console.</summary>
public sealed record RunRequest(string State, int Year)
{
    /// <summary>Pipeline data root containing input/ (default: repo data/).</summary>
    public string? InputRoot { get; init; }

    /// <summary>
    /// Directory that contains &lt;xx&gt;/ output folders. Null means no files are written:
    /// the staging database is the store of record.
    /// </summary>
    public string? OutputRoot { get; init; }

    /// <summary>Legacy --out: a combined data root with input/ and output/.</summary>
    public string? LegacyOutRoot { get; init; }

    /// <summary>Only produce a run for this election date (yyyy-MM-dd). Null means every election found.</summary>
    public string? ElectionDate { get; init; }

    public bool DryRun { get; init; }
    public string? Wayback { get; init; }

    /// <summary>
    /// Capture directory name under data/raw/&lt;xx&gt;/ (a pass id, or dry-... for a dry run) to
    /// serve fetches from instead of the network. The year comes from the capture.
    /// </summary>
    public string? Replay { get; init; }

    /// <summary>Capture directories to keep per state after a pass. Zero keeps them all.</summary>
    public int KeepRaw { get; init; } = 3;

    /// <summary>Record the pass and its per-election runs in the staging schema. On by default.</summary>
    public bool Persist { get; init; } = true;

    public string RequestedBy { get; init; } = Environment.UserName;

    /// <summary>"cli" or "web".</summary>
    public string Source { get; init; } = "cli";

    public string? CliArgs { get; init; }

    /// <summary>When the console queued the pass, the CollectionPasses row already exists.</summary>
    public int? ExistingPassId { get; init; }

    /// <summary>True when the caller asked for files as well as the database.</summary>
    public bool WriteFiles => OutputRoot is not null || LegacyOutRoot is not null;
}

/// <summary>One election's run inside a pass.</summary>
public sealed record ElectionRun(
    int RunId,
    string ElectionDate,
    string ElectionType,
    string ElectionName,
    bool IsPending,
    int CandidateCount,
    int MeasureCount,
    int CountyBallotCount);

/// <summary>What a collection pass produced.</summary>
public sealed record RunOutcome(
    Core.CollectResult Result,
    int? PassId,
    IReadOnlyList<ElectionRun> Runs,
    int UnassignedRowCount,
    bool FilesWritten,
    string? StateOutputDir);

/// <summary>A pass's raw capture as recorded on CollectionPasses and PassFetches.</summary>
public sealed record RawCapture(
    string RawDir,
    IReadOnlyList<Core.Raw.FetchLogEntry> Fetches,
    string? FetchLogSha256,
    int? ReplayOfPassId);

/// <summary>A pass could not start (unknown state, no collector, bad roots). Exit code 2 territory.</summary>
public sealed class RunSetupException(string message) : Exception(message);
