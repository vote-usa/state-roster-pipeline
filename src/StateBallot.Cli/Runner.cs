using StateBallot.Core;
using StateBallot.States.Ca;
using StateBallot.States.Tx;
using StateBallot.States.Wa;
using StateBallot.States.Wv;

namespace StateBallot.Cli;

public static class Runner
{
    // Keep project references rooted so state assemblies copy to the output directory.
    private static readonly Type[] RootedCollectors =
        [typeof(CaCollector), typeof(WaCollector), typeof(TxCollector), typeof(WvCollector)];

    public static async Task<int> RunAsync(string[] args)
    {
        _ = RootedCollectors;

        var state = "WA";
        int year = DateTime.UtcNow.Year;
        string? inputRootArg = null;
        string? outputRootArg = null;
        // Legacy: --out sets both roots (pipeline-style data/ with input/ + output/).
        string? outRoot = null;
        var dryRun = false;
        string? wayback = null;

        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--state" when i + 1 < args.Length:
                    state = args[++i].ToUpperInvariant();
                    break;
                case "--year" when i + 1 < args.Length:
                    year = int.Parse(args[++i]);
                    break;
                case "--out" when i + 1 < args.Length:
                    outRoot = args[++i];
                    break;
                case "--input-root" when i + 1 < args.Length:
                    inputRootArg = args[++i];
                    break;
                case "--output-root" when i + 1 < args.Length:
                    outputRootArg = args[++i];
                    break;
                case "--dry-run":
                    dryRun = true;
                    break;
                case "--wayback" when i + 1 < args.Length:
                    wayback = args[++i];
                    break;
                case "--help" or "-h":
                    Console.WriteLine($"""
                        StateBallot.Cli - state ballot roster collector

                        Options:
                          --state <XX>         Two-letter state code (default: WA; implemented: {ImplementedStates()})
                          --year <yyyy>        Target election year (default: current UTC year)
                          --out <dir>          Pipeline data root with input/ + output/ (default: data/)
                          --input-root <dir>   Pipeline data root for inputs (overrides --out for reads)
                          --output-root <dir>  Roster output root; writes <dir>/<state>/ (default: <data>/output)
                          --dry-run            Fetch sources and report counts without writing files
                          --wayback <ts>       Replay sources via web.archive.org at this timestamp
                                               (yyyyMMdd or yyyyMMddHHmmss; nearest capture is served)

                        Snapshot publishes use --input-root pointing at this repo's data/ and
                        --output-root pointing at a checkout of vote-usa/state-roster-data.
                        """);
                    return 0;
                default:
                    Console.Error.WriteLine($"Unknown argument: {args[i]}");
                    return 2;
            }
        }

        var pipelineDataRoot = inputRootArg ?? outRoot ?? FindDataRoot();
        var outputRoot = outputRootArg
            ?? (outRoot is not null ? DataPaths.OutputRoot(outRoot) : DataPaths.OutputRoot(pipelineDataRoot));

        StateCatalog catalog;
        try
        {
            catalog = StateCatalog.LoadFromDataRoot(pipelineDataRoot);
        }
        catch (InvalidOperationException ex)
        {
            var repoData = FindDataRoot();
            if (repoData == pipelineDataRoot)
            {
                Console.Error.WriteLine(ex.Message);
                return 2;
            }
            catalog = StateCatalog.LoadFromDataRoot(repoData);
            pipelineDataRoot = repoData;
        }

        if (!catalog.TryGet(state, out var entry))
        {
            Console.Error.WriteLine(
                $"State '{state}' is not in data/input/state_catalog.json. Known codes: {string.Join(", ", catalog.Codes.Order())}.");
            return 2;
        }

        if (!StateCatalog.IsImplemented(entry.Status))
        {
            Console.Error.WriteLine(
                $"State '{state}' ({entry.Name}) is in the catalog but not implemented yet. " +
                $"Implemented: {string.Join(", ", catalog.ImplementedCodes.Order())}. " +
                "See logs/adding-a-state.md.");
            return 2;
        }

        var collectors = CollectorDiscovery.Discover();
        if (!collectors.TryGetValue(state, out var factory))
        {
            Console.Error.WriteLine(
                $"State '{state}' is marked implemented in the catalog but no [StateCode(\"{state}\")] " +
                "collector was discovered. Ensure the state project is referenced by the Cli and its DLL " +
                "is copied to the output directory.");
            return 2;
        }

        var stateOutputDir = DataPaths.StateOutputDir(outputRoot, state);
        var inputDataRoot = Path.GetFullPath(pipelineDataRoot);

        using var fetcher = new HttpFetcher();
        if (wayback is not null)
        {
            Console.WriteLine($"Wayback replay: rewriting fetches to web.archive.org captures near {wayback}.");
            fetcher.RewriteUrl = url =>
                url.Contains("web.archive.org", StringComparison.OrdinalIgnoreCase)
                    ? url
                    : $"https://web.archive.org/web/{wayback}id_/{url}";
        }
        var collector = factory(fetcher, year, stateOutputDir, inputDataRoot);
        var result = await collector.CollectAsync();

        result.PrintSummary(Console.Out);

        if (dryRun)
        {
            Console.WriteLine("\nDry run - no files written.");
            return 0;
        }

        new ResultWriter(stateOutputDir, DataPaths.SourcesPath(inputDataRoot, state)).WriteAll(result);
        Console.WriteLine($"\nOutputs written to {Path.GetFullPath(stateOutputDir)}");
        Console.WriteLine($"Sources written to {Path.GetFullPath(DataPaths.SourcesPath(inputDataRoot, state))}");
        return 0;
    }

    private static string ImplementedStates() =>
        string.Join(", ", CollectorDiscovery.Discover().Keys.Order());

    /// <summary>Walks up from the executable to the repo root (has src/ and data/ side by side).</summary>
    private static string FindDataRoot()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
        {
            if (Directory.Exists(Path.Combine(d.FullName, "src")) && Directory.Exists(Path.Combine(d.FullName, "data")))
                return Path.Combine(d.FullName, "data");
        }
        return Path.Combine(Environment.CurrentDirectory, "data");
    }
}
