using StateBallot.Core;
using StateBallot.States.Ca;
using StateBallot.States.Wa;

namespace StateBallot.Cli;

public static class Runner
{
    /// <summary>
    /// Registry of implemented states. To add a state, implement IStateCollector
    /// in a StateBallot.States.<Xx> project and register its factory here.
    /// </summary>
    private static readonly Dictionary<string, Func<HttpFetcher, int, string, IStateCollector>> StateCollectors =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["CA"] = (fetcher, year, stateDataDir) => new CaCollector(fetcher, year, stateDataDir),
            ["WA"] = (fetcher, year, stateDataDir) => new WaCollector(fetcher, year, stateDataDir),
        };

    public static async Task<int> RunAsync(string[] args)
    {
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
                          --out <dir>     Output root; per-state files go in <dir>/<state> (default: StateBallot/data)
                          --dry-run       Fetch sources and report counts without writing files
                        """);
                    return 0;
                default:
                    Console.Error.WriteLine($"Unknown argument: {args[i]}");
                    return 2;
            }
        }

        if (!StateCollectors.TryGetValue(state, out var factory))
        {
            Console.Error.WriteLine(
                $"State '{state}' is not implemented yet. Implemented states: {ImplementedStates()}");
            return 2;
        }

        var stateDataDir = Path.Combine(outRoot ?? FindDataRoot(), state.ToLowerInvariant());

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

    private static string ImplementedStates() => string.Join(", ", StateCollectors.Keys.Order());

    /// <summary>Walks up from the executable to the StateBallot root (has src/ and data/ side by side).</summary>
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
