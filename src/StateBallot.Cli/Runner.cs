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
        string? normalize = null;
        var captureOnly = false;
        var keepRaw = 3;
        string? inputRootArg = null;
        string? outputRootArg = null;
        // Legacy: --out sets both roots (pipeline-style data/ with input/ + output/).
        string? outRoot = null;
        string? electionDate = null;
        var dryRun = false;
        string? wayback = null;
        var migrate = false;
        string? linksCommand = null;
        string? linksState = null;
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
                case "--normalize" when i + 1 < args.Length:
                    normalize = args[++i];
                    break;
                case "--capture-only":
                    captureOnly = true;
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
                case "--export-links" or "--reseed-links":
                    linksCommand = args[i];
                    if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                        linksState = args[++i].ToUpperInvariant();
                    break;
                case "--triggered-by" when i + 1 < args.Length:
                    triggeredBy = args[++i];
                    break;
                case "--help" or "-h":
                    Console.WriteLine($"""
                        StateBallot.Cli - state ballot roster collector

                        Runs in two stages. Capture fetches every source page for a state and year and saves
                        it as fetched under data/raw/<state>/<capture id>/ with fetch_log.json. Normalize then
                        builds the roster from that saved capture alone and stores one run per election in the
                        staging database. Files are only written when an output root is given.

                        Options:
                          --state <XX>         Two-letter state code (default: WA; implemented: {ImplementedStates()})
                          --year <yyyy>        Target election year (default: current UTC year)
                          --election <date>    Store only this election (yyyy-MM-dd); default is every one found
                          --dry-run            Fetch and report counts, store nothing
                          --wayback <ts>       Capture sources via web.archive.org at this timestamp
                                               (yyyyMMdd or yyyyMMddHHmmss; nearest capture is served)
                          --capture-only       Stop after the capture stage
                          --normalize <id>     Skip capture and normalize an existing capture (a capture id, or
                                               dry-... from a dry run). The year comes from the capture.
                          --keep-raw <n>       Raw capture directories to keep per state (default: 3, 0 keeps all)
                          --triggered-by <who> Name recorded on the pass (default: current OS user)
                          --input-root <dir>   Pipeline data root for inputs (default: repo data/)
                          --output-root <dir>  Also export files to <dir>/<state>/ (e.g. a state-roster-data checkout)
                          --out <dir>          Legacy: a data root with input/ + output/, exports files too
                          --migrate            Apply pending migrations to the staging schema, seed source links that
                                               are missing from data/input/<xx>/source_links.json, and exit.
                                               Links already in the database are kept, and drift is reported.
                          --export-links [XX]  Write the database's source links back to the seed files (one state, or all)
                          --reseed-links [XX]  Replace the database's source links with the seed files (one state, or all).
                                               Rows only in the database are deleted.

                        The staging connection comes from ROSTER_STAGING_CONNECTION and defaults to
                        the local Docker MySQL in db/. Run --migrate once before the first collection.
                        """);
                    return 0;
                default:
                    Console.Error.WriteLine($"Unknown argument: {args[i]}");
                    return 2;
            }
        }

        if (normalize is not null && yearGiven)
        {
            Console.Error.WriteLine("--normalize takes the year from the capture. Drop --year.");
            return 2;
        }

        var db = StagingDb.FromEnvironment();

        if (migrate)
            return await MigrateAsync(db, inputRootArg);
        if (linksCommand is not null)
            return await LinksAsync(db, linksCommand, linksState, inputRootArg);

        var request = new RunRequest(state, year)
        {
            InputRoot = inputRootArg,
            OutputRoot = outputRootArg,
            LegacyOutRoot = outRoot,
            ElectionDate = electionDate,
            DryRun = dryRun,
            Wayback = wayback,
            Normalize = normalize,
            CaptureOnly = captureOnly,
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

    private static async Task<int> MigrateAsync(StagingDb db, string? inputRoot)
    {
        Console.WriteLine($"Migrating {StagingDb.Describe(db.StagingConnectionString)}");
        var report = await new Migrator(db.StagingConnectionString, inputRoot).ApplyAsync();
        Console.WriteLine(
            $"Applied: {report.Applied}; already applied: {report.AlreadyApplied}; at version {report.CurrentVersion}.");
        Console.WriteLine($"Source links: {report.Links.Inserted} seeded, {report.Links.Unchanged} already up to date.");
        foreach (var note in report.Links.Notes)
            Console.WriteLine($"  {note}");
        return 0;
    }

    private static async Task<int> LinksAsync(StagingDb db, string command, string? state, string? inputRoot)
    {
        var dataRoot = inputRoot ?? CollectorRunner.FindDataRoot();
        var store = new LinkStore(db.StagingConnectionString);
        if (command == "--export-links")
        {
            var written = await store.ExportAsync(dataRoot, state);
            Console.WriteLine(written.Count == 0
                ? $"No source links in {StagingDb.Describe(db.StagingConnectionString)} to export."
                : $"Exported {string.Join(", ", written)} to {DataPaths.InputRoot(dataRoot)}/<xx>/{SourceLinkSet.FileName}.");
        }
        else
        {
            var reseeded = await store.ReseedAsync(dataRoot, state);
            Console.WriteLine($"Reseeded {string.Join(", ", reseeded)} from {DataPaths.InputRoot(dataRoot)}/<xx>/{SourceLinkSet.FileName}.");
        }
        return 0;
    }

    private static string ImplementedStates() =>
        string.Join(", ", CollectorDiscovery.Discover().Keys.Order());
}
