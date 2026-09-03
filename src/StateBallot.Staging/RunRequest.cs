namespace StateBallot.Staging;

/// <summary>Everything needed to execute one collector run, from the CLI or the console.</summary>
public sealed record RunRequest(string State, int Year)
{
    /// <summary>Pipeline data root containing input/ (default: repo data/).</summary>
    public string? InputRoot { get; init; }

    /// <summary>Directory that contains &lt;xx&gt;/ output folders (default: &lt;data&gt;/output).</summary>
    public string? OutputRoot { get; init; }

    /// <summary>Legacy --out: a combined data root with input/ and output/.</summary>
    public string? LegacyOutRoot { get; init; }

    public bool DryRun { get; init; }
    public string? Wayback { get; init; }

    /// <summary>Write the run into the staging schema after the files.</summary>
    public bool Persist { get; init; }

    public string RequestedBy { get; init; } = Environment.UserName;

    /// <summary>"cli" or "web".</summary>
    public string Source { get; init; } = "cli";

    public string? CliArgs { get; init; }

    /// <summary>When the console queued the run, the Runs row already exists.</summary>
    public int? ExistingRunId { get; init; }
}

/// <summary>What a run produced.</summary>
public sealed record RunOutcome(
    Core.CollectResult Result,
    string StateOutputDir,
    string SourcesPath,
    bool FilesWritten,
    int? RunId);

/// <summary>A run could not start (unknown state, no collector, bad roots). Exit code 2 territory.</summary>
public sealed class RunSetupException(string message) : Exception(message);
