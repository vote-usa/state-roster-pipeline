using StateBallot.Core;
using StateBallot.Staging;

namespace StateBallot.Cli;

public static class Runner
{
    public static async Task<int> RunAsync(string[] args)
    {
        var state = "WA";
        int year = DateTime.UtcNow.Year;
        string? inputRootArg = null;
        string? outputRootArg = null;
        // Legacy: --out sets both roots (pipeline-style data/ with input/ + output/).
        string? outRoot = null;
        var dryRun = false;
        string? wayback = null;
        var migrate = false;
        var persist = false;
        string? triggeredBy = null;

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
                case "--migrate":
                    migrate = true;
                    break;
                case "--persist":
                    persist = true;
                    break;
                case "--triggered-by" when i + 1 < args.Length:
                    triggeredBy = args[++i];
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
                          --persist            Also record the run in the staging schema (ignored with --dry-run)
                          --triggered-by <who> Name recorded on the staging run (default: current OS user)
                          --migrate            Apply db/migrations/*.sql to the staging schema and exit
                                               (connection from ROSTER_STAGING_CONNECTION; local Docker default)

                        Snapshot publishes use --input-root pointing at this repo's data/ and
                        --output-root pointing at a checkout of vote-usa/state-roster-data.
                        """);
                    return 0;
                default:
                    Console.Error.WriteLine($"Unknown argument: {args[i]}");
                    return 2;
            }
        }

        var db = StagingDb.FromEnvironment();

        if (migrate)
            return await MigrateAsync(db);

        var request = new RunRequest(state, year)
        {
            InputRoot = inputRootArg,
            OutputRoot = outputRootArg,
            LegacyOutRoot = outRoot,
            DryRun = dryRun,
            Wayback = wayback,
            Persist = persist && !dryRun,
            RequestedBy = triggeredBy ?? Environment.UserName,
            Source = "cli",
            CliArgs = string.Join(' ', args),
        };

        if (persist && dryRun)
            Console.WriteLine("Note: --persist is ignored with --dry-run.");

        try
        {
            await new CollectorRunner(db).RunAsync(request, Console.Out);
            return 0;
        }
        catch (RunSetupException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 2;
        }
        catch (Exception ex)
        {
            // Critical failure (source unreachable, empty page that should have data, IO).
            // A staging run, if begun, has already been marked failed with the full error.
            Console.Error.WriteLine($"Run failed: {ex.Message}");
            return 1;
        }
    }

    private static async Task<int> MigrateAsync(StagingDb db)
    {
        var dir = Migrator.FindMigrationsDir();
        Console.WriteLine($"Migrating {StagingDb.Describe(db.StagingConnectionString)} from {dir}");
        var report = await new Migrator(db.StagingConnectionString, dir).ApplyAsync(Console.Out);
        Console.WriteLine($"Applied: {report.Applied.Count}; already applied: {report.AlreadyApplied.Count}.");
        return 0;
    }

    private static string ImplementedStates() =>
        string.Join(", ", CollectorDiscovery.Discover().Keys.Order());
}
