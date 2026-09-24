using StateBallot.Core;
using StateBallot.Staging;

namespace StateBallot.Cli;

public static class Runner
{
    public static async Task<int> RunAsync(string[] args)
    {
        var state = "WA";
        int year = DateTime.UtcNow.Year;
        var yearGiven = false;
        string? replay = null;
        var keepRaw = 3;
        string? inputRootArg = null;
        string? outputRootArg = null;
        // Legacy: --out sets both roots (pipeline-style data/ with input/ + output/).
        string? outRoot = null;
        string? electionDate = null;
        var dryRun = false;
        string? wayback = null;
        var migrate = false;
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
                    yearGiven = true;
                    break;
                case "--replay" when i + 1 < args.Length:
                    replay = args[++i];
                    break;
                case "--keep-raw" when i + 1 < args.Length:
                    keepRaw = int.Parse(args[++i]);
                    break;
                case "--election" when i + 1 < args.Length:
                    electionDate = args[++i];
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
                case "--triggered-by" when i + 1 < args.Length:
                    triggeredBy = args[++i];
                    break;
                case "--help" or "-h":
                    Console.WriteLine($"""
                        StateBallot.Cli - state ballot roster collector

                        Collects a state and year in one pass, then stores one run per election
                        in the staging database. Files are only written when an output root is given.
                        Every fetch is saved as fetched under data/raw/<state>/<pass id>/ with fetch_log.json.

                        Options:
                          --state <XX>         Two-letter state code (default: WA; implemented: {ImplementedStates()})
                          --year <yyyy>        Target election year (default: current UTC year)
                          --election <date>    Store only this election (yyyy-MM-dd); default is every one found
                          --dry-run            Fetch and report counts, store nothing
                          --wayback <ts>       Replay sources via web.archive.org at this timestamp
                                               (yyyyMMdd or yyyyMMddHHmmss; nearest capture is served)
                          --replay <capture>   Re-run against a saved capture in data/raw/<state>/<capture>/
                                               (a pass id, or dry-... from a dry run). No network is used,
                                               and the year comes from the capture.
                          --keep-raw <n>       Raw capture directories to keep per state (default: 3, 0 keeps all)
                          --triggered-by <who> Name recorded on the pass (default: current OS user)
                          --input-root <dir>   Pipeline data root for inputs (default: repo data/)
                          --output-root <dir>  Also export files to <dir>/<state>/ (e.g. a state-roster-data checkout)
                          --out <dir>          Legacy: a data root with input/ + output/, exports files too
                          --migrate            Apply pending migrations to the staging schema and exit

                        The staging connection comes from ROSTER_STAGING_CONNECTION and defaults to
                        the local Docker MySQL in db/. Run --migrate once before the first collection.
                        """);
                    return 0;
                default:
                    Console.Error.WriteLine($"Unknown argument: {args[i]}");
                    return 2;
            }
        }

        if (replay is not null && yearGiven)
        {
            Console.Error.WriteLine("--replay takes the year from the capture. Drop --year.");
            return 2;
        }

        var db = StagingDb.FromEnvironment();

        if (migrate)
            return await MigrateAsync(db);

        var request = new RunRequest(state, year)
        {
            InputRoot = inputRootArg,
            OutputRoot = outputRootArg,
            LegacyOutRoot = outRoot,
            ElectionDate = electionDate,
            DryRun = dryRun,
            Wayback = wayback,
            Replay = replay,
            KeepRaw = keepRaw,
            RequestedBy = triggeredBy ?? Environment.UserName,
            Source = "cli",
            CliArgs = string.Join(' ', args),
        };

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
            // The pass, if begun, has already been marked failed with the full error.
            Console.Error.WriteLine($"Run failed: {ex.Message}");
            return 1;
        }
    }

    private static async Task<int> MigrateAsync(StagingDb db)
    {
        Console.WriteLine($"Migrating {StagingDb.Describe(db.StagingConnectionString)}");
        var report = await new Migrator(db.StagingConnectionString).ApplyAsync();
        Console.WriteLine(
            $"Applied: {report.Applied}; already applied: {report.AlreadyApplied}; at version {report.CurrentVersion}.");
        return 0;
    }

    private static string ImplementedStates() =>
        string.Join(", ", CollectorDiscovery.Discover().Keys.Order());
}
