namespace StateBallot.Core;

/// <summary>
/// Resolves input vs output paths under the data root.
/// Inputs: data/input/ (catalog, county_fips, sources).
/// Outputs: data/output/&lt;xx&gt;/ (candidates, elections, …).
/// </summary>
public static class DataPaths
{
    public static string InputRoot(string dataRoot) => Path.Combine(dataRoot, "input");

    public static string OutputRoot(string dataRoot) => Path.Combine(dataRoot, "output");

    public static string StateCatalogPath(string dataRoot) =>
        Path.Combine(InputRoot(dataRoot), "state_catalog.json");

    /// <summary>Per-state input dir: data/input/&lt;xx&gt;/ (county_fips.json, sources.json).</summary>
    public static string StateInputDir(string dataRoot, string stateCode) =>
        Path.Combine(InputRoot(dataRoot), stateCode.ToLowerInvariant());

    /// <summary>Per-state output dir: data/output/&lt;xx&gt;/ (candidates, elections, …).</summary>
    public static string StateOutputDir(string dataRoot, string stateCode) =>
        Path.Combine(OutputRoot(dataRoot), stateCode.ToLowerInvariant());

    public static string CountyFipsPath(string dataRoot, string stateCode) =>
        Path.Combine(StateInputDir(dataRoot, stateCode), "county_fips.json");

    public static string SourcesPath(string dataRoot, string stateCode) =>
        Path.Combine(StateInputDir(dataRoot, stateCode), "sources.json");

    /// <summary>
    /// When a collector is given only the per-state output dir (data/output/ca),
    /// recover the data root and state code.
    /// </summary>
    public static (string DataRoot, string StateCode) FromStateOutputDir(string stateOutputDir)
    {
        var dir = new DirectoryInfo(Path.GetFullPath(stateOutputDir));
        var stateCode = dir.Name;
        var outputRoot = dir.Parent
            ?? throw new InvalidOperationException($"Cannot resolve output root from '{stateOutputDir}'.");
        if (!string.Equals(outputRoot.Name, "output", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"Expected per-state output dir under data/output/<xx>, got '{stateOutputDir}'.");
        var dataRoot = outputRoot.Parent?.FullName
            ?? throw new InvalidOperationException($"Cannot resolve data root from '{stateOutputDir}'.");
        return (dataRoot, stateCode);
    }
}
