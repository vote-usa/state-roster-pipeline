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
        string? outRoot = null;
        var dryRun = false;

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
                case "--dry-run":
                    dryRun = true;
                    break;
                case "--help" or "-h":
                    Console.WriteLine($"""
                        StateBallot.Cli - state ballot roster collector

                        Options:
                          --state <XX>    Two-letter state code (default: WA; implemented: {ImplementedStates()})
                          --year <yyyy>   Target election year (default: current UTC year)
                          --out <dir>     Output root; per-state files go in <dir>/<state> (default: data/)
                          --dry-run       Fetch sources and report counts without writing files
                        """);
                    return 0;
                default:
                    Console.Error.WriteLine($"Unknown argument: {args[i]}");
                    return 2;
            }
        }

        var dataRoot = outRoot ?? FindDataRoot();
        StateCatalog catalog;
        try
        {
            catalog = StateCatalog.LoadFromDataRoot(dataRoot);
        }
        catch (InvalidOperationException ex)
        {
            var repoData = FindDataRoot();
            if (repoData == dataRoot)
            {
                Console.Error.WriteLine(ex.Message);
                return 2;
            }
            catalog = StateCatalog.LoadFromDataRoot(repoData);
        }

        if (!catalog.TryGet(state, out var entry))
        {
            Console.Error.WriteLine(
                $"State '{state}' is not in data/state_catalog.json. Known codes: {string.Join(", ", catalog.Codes.Order())}.");
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

        var stateDataDir = Path.Combine(dataRoot, state.ToLowerInvariant());

        using var fetcher = new HttpFetcher();
        var collector = factory(fetcher, year, stateDataDir);
        var result = await collector.CollectAsync();

        result.PrintSummary(Console.Out);

        if (dryRun)
        {
            Console.WriteLine("\nDry run - no files written.");
            return 0;
        }

        new ResultWriter(stateDataDir).WriteAll(result);
        Console.WriteLine($"\nOutputs written to {Path.GetFullPath(stateDataDir)}");
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
