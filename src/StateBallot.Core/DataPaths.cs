namespace StateBallot.Core;

/// <summary>
/// Resolves input vs output paths.
/// Pipeline default: input under data/input/, output under data/output/&lt;xx&gt;/.
/// Snapshot publishes can point --output-root at a checkout of state-roster-data
/// (states at the repo root: &lt;output-root&gt;/&lt;xx&gt;/).
/// </summary>
public static class DataPaths
{
    public const string DefaultDataRepoUrl = "https://github.com/vote-usa/state-roster-data.git";

    public static string InputRoot(string inputDataRoot) => Path.Combine(inputDataRoot, "input");

    public static string OutputRoot(string pipelineDataRoot) => Path.Combine(pipelineDataRoot, "output");

    public static string StateCatalogPath(string inputDataRoot) =>
        Path.Combine(InputRoot(inputDataRoot), "state_catalog.json");

    /// <summary>Per-state input dir: &lt;inputDataRoot&gt;/input/&lt;xx&gt;/.</summary>
    public static string StateInputDir(string inputDataRoot, string stateCode) =>
        Path.Combine(InputRoot(inputDataRoot), stateCode.ToLowerInvariant());

    /// <summary>Per-state output dir: &lt;outputRoot&gt;/&lt;xx&gt;/.</summary>
    public static string StateOutputDir(string outputRoot, string stateCode) =>
        Path.Combine(outputRoot, stateCode.ToLowerInvariant());

    public static string CountyFipsPath(string inputDataRoot, string stateCode) =>
        Path.Combine(StateInputDir(inputDataRoot, stateCode), "county_fips.json");

    public static string SourcesPath(string inputDataRoot, string stateCode) =>
        Path.Combine(StateInputDir(inputDataRoot, stateCode), "sources.json");

    public static string SnapshotPath(string inputDataRoot) =>
        Path.Combine(InputRoot(inputDataRoot), "snapshot.json");

    /// <summary>
    /// When output lives at data/output/&lt;xx&gt;, the pipeline data root is the grandparent.
    /// Returns null when the layout does not match (e.g. a flat state-roster-data checkout).
    /// </summary>
    public static string? TryInferPipelineDataRoot(string stateOutputDir)
    {
        var dir = new DirectoryInfo(Path.GetFullPath(stateOutputDir));
        var outputRoot = dir.Parent;
        if (outputRoot is null ||
            !string.Equals(outputRoot.Name, "output", StringComparison.OrdinalIgnoreCase))
            return null;
        return outputRoot.Parent?.FullName;
    }
}
