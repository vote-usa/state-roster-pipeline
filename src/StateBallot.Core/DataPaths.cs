namespace StateBallot.Core;

/// <summary>
/// Resolves input vs output paths.
/// Pipeline default: input under data/input/ (catalog, county_fips, sources,
/// date_formats, election_type_names, selectors, …), output under
/// data/output/&lt;xx&gt;/. Snapshot publishes can point --output-root at a
/// checkout of state-roster-data (states at the repo root: &lt;output-root&gt;/&lt;xx&gt;/).
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

    public static string DateFormatsPath(string inputDataRoot, string stateCode) =>
        Path.Combine(StateInputDir(inputDataRoot, stateCode), "date_formats.json");

    public static string ElectionTypeNamesPath(string inputDataRoot, string stateCode) =>
        Path.Combine(StateInputDir(inputDataRoot, stateCode), "election_type_names.json");

    public static string CandidateFieldMapPath(string inputDataRoot, string stateCode) =>
        Path.Combine(StateInputDir(inputDataRoot, stateCode), "candidate_field_map.json");

    /// <summary>
    /// For a state whose source exposes no way to discover its own current
    /// election ids (no index page, no predictable URL template) - a
    /// hand-maintained canonical type name (e.g. "General") -> the source's
    /// own id, re-derived by a human each cycle. See NmSourceConfig for the
    /// first consumer.
    /// </summary>
    public static string ElectionIdsPath(string inputDataRoot, string stateCode) =>
        Path.Combine(StateInputDir(inputDataRoot, stateCode), "election_ids.json");

    public static string SelectorsPath(string inputDataRoot, string stateCode) =>
        Path.Combine(StateInputDir(inputDataRoot, stateCode), "selectors.json");

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

    /// <summary>
    /// When a collector is given only the per-state output dir (data/output/ca),
    /// recover the data root and state code.
    /// </summary>
    /// <remarks>
    /// Compatibility shim for collectors not yet migrated to the explicit
    /// --input-root / --output-root split (<see cref="TryInferPipelineDataRoot"/>).
    /// New collectors should take an explicit inputDataRoot instead of calling this.
    /// </remarks>
    public static (string DataRoot, string StateCode) FromStateOutputDir(string stateOutputDir)
    {
        var stateCode = new DirectoryInfo(Path.GetFullPath(stateOutputDir)).Name;
        var dataRoot = TryInferPipelineDataRoot(stateOutputDir)
            ?? throw new InvalidOperationException(
                $"Expected per-state output dir under data/output/<xx>, got '{stateOutputDir}'.");
        return (dataRoot, stateCode);
    }
}
