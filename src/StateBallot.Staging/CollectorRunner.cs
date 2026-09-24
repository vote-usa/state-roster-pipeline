using System.Diagnostics;
using System.Globalization;
using StateBallot.Core;
using StateBallot.Core.Raw;
using StateBallot.States.Ca;
using StateBallot.States.Tx;
using StateBallot.States.Wa;
using StateBallot.States.Wv;

namespace StateBallot.Staging;

/// <summary>
/// Executes one collection pass end to end: catalog check, discovery, collect, record the
/// pass and its per-election runs in the staging schema, and optionally also export files.
/// The staging database is the store of record. Files are written only when the caller
/// names an output root, which is how the data-repo export still works.
/// Shared by the CLI and the console's background job runner.
/// </summary>
public sealed class CollectorRunner
{
    // Keep project references rooted so state assemblies copy to the output directory.
    private static readonly Type[] RootedCollectors =
        [typeof(CaCollector), typeof(WaCollector), typeof(TxCollector), typeof(WvCollector)];

    private readonly StagingDb? _db;

    /// <param name="db">Required when a request asks to persist.</param>
    public CollectorRunner(StagingDb? db = null)
    {
        _ = RootedCollectors;
        _db = db;
    }

    public async Task<RunOutcome> RunAsync(RunRequest request, TextWriter log, CancellationToken ct = default)
    {
        var state = request.State.ToUpperInvariant();
        var (pipelineDataRoot, outputRoot) = ResolveRoots(request);

        var catalog = StateCatalog.LoadFromDataRoot(pipelineDataRoot);
        if (!catalog.TryGet(state, out var entry))
            throw new RunSetupException(
                $"State '{state}' is not in data/input/state_catalog.json. Known codes: {string.Join(", ", catalog.Codes.Order())}.");

        if (!StateCatalog.IsImplemented(entry.Status))
            throw new RunSetupException(
                $"State '{state}' ({entry.Name}) is in the catalog but not implemented yet. " +
                $"Implemented: {string.Join(", ", catalog.ImplementedCodes.Order())}.");

        var collectors = CollectorDiscovery.Discover();
        if (!collectors.TryGetValue(state, out var factory))
            throw new RunSetupException(
                $"State '{state}' is marked implemented in the catalog but no [StateCode(\"{state}\")] " +
                "collector was discovered. Ensure the state project is referenced and its DLL is copied to the output directory.");

        if (request.ElectionDate is { } filterDate
            && !DateOnly.TryParseExact(filterDate, "yyyy-MM-dd", out _))
            throw new RunSetupException($"--election must be yyyy-MM-dd, got '{filterDate}'.");

        var stateRawDir = RawRetention.StateDir(pipelineDataRoot, state);
        RawReplay? replay = null;
        if (request.Replay is { } captureId)
        {
            if (request.Wayback is not null)
                throw new RunSetupException("--replay serves a saved capture and cannot be combined with --wayback.");
            var captureDir = Path.Combine(stateRawDir, captureId);
            if (!File.Exists(Path.Combine(captureDir, RawSink.LogFileName)))
                throw new RunSetupException(
                    $"No capture at {captureDir}. It may have been pruned by --keep-raw, " +
                    "in which case PassFetches still holds its hashes but the payloads are gone.");
            replay = new RawReplay(captureDir);
            if (!string.Equals(replay.Log.State, state, StringComparison.OrdinalIgnoreCase))
                throw new RunSetupException($"Capture {captureId} is for {replay.Log.State}, not {state}.");
            request = request with { Year = replay.Log.Year };
        }

        var persist = request.Persist && !request.DryRun;
        if (persist && _db is null)
            throw new RunSetupException("No staging database was configured. Pass --dry-run to collect without storing.");

        var inputDataRoot = Path.GetFullPath(pipelineDataRoot);
        var stateOutputDir = outputRoot is null ? null : DataPaths.StateOutputDir(outputRoot, state);

        RunWriter? writer = null;
        int? passId = null;
        RawSink? sink = null;
        if (persist)
        {
            writer = new RunWriter(_db!);
            passId = await writer.BeginPassAsync(request, TryGetGitSha(inputDataRoot), ct);
            log.WriteLine($"Collection pass {passId} ({StagingDb.Describe(_db!.StagingConnectionString)})");
        }

        // Collectors write progress with Console.WriteLine. Tee the console into a buffer
        // so the pass log can be stored. Process-global: one pass at a time per process.
        var captured = new StringWriter();
        var originalOut = Console.Out;
        var tee = new TeeTextWriter(log, captured);
        Console.SetOut(tee);
        try
        {
            using var fetcher = new HttpFetcher();
            if (replay is not null)
            {
                fetcher.Replay = replay;
                tee.WriteLine(
                    $"Replaying capture {replay.Log.CaptureId} ({replay.Log.Fetches.Count} fetch(es), {replay.Log.Year}) " +
                    $"from {replay.Directory}. The network is not used.");
            }
            else
            {
                sink = StartCapture(stateRawDir, passId, state, request.Year, tee);
                fetcher.RawSink = sink;
            }

            if (request.Wayback is not null)
            {
                tee.WriteLine($"Wayback replay: rewriting fetches to web.archive.org captures near {request.Wayback}.");
                var stamp = request.Wayback;
                fetcher.RewriteUrl = url =>
                    url.Contains("web.archive.org", StringComparison.OrdinalIgnoreCase)
                        ? url
                        : $"https://web.archive.org/web/{stamp}id_/{url}";
            }

            // The sources publish a whole state and year in one fetch, so the collect stays
            // state-scoped. The result is then narrowed and fanned out per election.
            var collector = factory(fetcher, request.Year, stateOutputDir ?? inputDataRoot, inputDataRoot);
            var collected = await collector.CollectAsync();
            collected.Sources.AttachPayloadHashes((IEnumerable<FetchLogEntry>?)replay?.Served ?? sink!.Entries);
            if (replay is { UnusedCount: > 0 })
                tee.WriteLine($"Warning: {replay.UnusedCount} captured fetch(es) were never requested during replay.");

            var result = collected;
            if (request.ElectionDate is { } wanted)
            {
                var available = CollectResultFilter.ElectionDates(collected);
                result = CollectResultFilter.ToElectionDate(collected, wanted);
                if (result.Elections.Count == 0)
                    throw new RunSetupException(
                        $"No {state} election on {wanted} for {request.Year}. " +
                        $"Found: {(available.Count == 0 ? "none" : string.Join(", ", available))}.");
                tee.WriteLine($"Filtered to the {wanted} election.");
            }

            var summary = new StringWriter();
            result.PrintSummary(summary);
            tee.Write(summary.ToString());
            if (sink is not null)
                tee.WriteLine($"Raw capture: {sink.Entries.Count} fetch(es), {sink.TotalBytes:N0} bytes in {sink.Directory}");

            var filesWritten = false;
            if (stateOutputDir is not null && !request.DryRun)
            {
                var sourcesPath = DataPaths.SourcesPath(inputDataRoot, state);
                new ResultWriter(stateOutputDir, sourcesPath).WriteAll(result);
                filesWritten = true;
                tee.WriteLine($"\nFiles exported to {Path.GetFullPath(stateOutputDir)}");
            }

            IReadOnlyList<ElectionRun> runs = [];
            var unassigned = 0;
            if (writer is not null)
            {
                await writer.RecordRawAsync(passId!.Value, DescribeRaw(sink, replay, pipelineDataRoot), ct);
                tee.Flush();
                var stored = await writer.CompletePassAsync(
                    passId!.Value, result, summary.ToString(), captured.ToString(), ct);
                runs = stored.Runs;
                unassigned = stored.UnassignedRowCount;

                tee.WriteLine($"\nPass {passId} stored {runs.Count} election run(s):");
                foreach (var run in runs)
                {
                    var pending = run.IsPending ? ", pending at the source" : "";
                    tee.WriteLine(
                        $"  run {run.RunId}  {run.ElectionDate}  {run.ElectionType,-16} " +
                        $"{run.CandidateCount} candidate(s), {run.MeasureCount} measure(s), " +
                        $"{run.CountyBallotCount} county ballot(s){pending}");
                }
                if (unassigned > 0)
                    tee.WriteLine($"  {unassigned} row(s) could not be matched to an election, see PassUnassignedRows.");
            }
            else if (request.DryRun)
            {
                tee.WriteLine("\nDry run - nothing stored.");
            }

            return new RunOutcome(result, passId, runs, unassigned, filesWritten, stateOutputDir);
        }
        catch (Exception ex) when (writer is not null && ex is not RunSetupException)
        {
            tee.Flush();
            if (sink is not null || replay is not null)
                await writer.RecordRawAsync(passId!.Value, DescribeRaw(sink, replay, pipelineDataRoot), CancellationToken.None);
            await writer.FailPassAsync(passId!.Value, ex.ToString(), captured.ToString(), CancellationToken.None);
            throw;
        }
        finally
        {
            Console.SetOut(originalOut);
            if (sink is not null)
                PruneRaw(stateRawDir, request.KeepRaw, log);
        }
    }

    /// <summary>
    /// Opens data/raw/&lt;xx&gt;/&lt;pass-id&gt;/, or dry-&lt;utc stamp&gt; when nothing is stored.
    /// A directory left over from a database that was since recreated is moved aside, not overwritten.
    /// </summary>
    private static RawSink StartCapture(string stateRawDir, int? passId, string state, int year, TextWriter log)
    {
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        var captureId = passId?.ToString(CultureInfo.InvariantCulture) ?? $"dry-{stamp}";
        var dir = Path.Combine(stateRawDir, captureId);
        if (Directory.Exists(dir))
        {
            var aside = $"{dir}-stale-{stamp}";
            Directory.Move(dir, aside);
            log.WriteLine($"Moved an older capture that reused this pass id to {aside}.");
        }
        return new RawSink(dir, captureId, state, year);
    }

    private static RawCapture DescribeRaw(RawSink? sink, RawReplay? replay, string pipelineDataRoot)
    {
        if (replay is not null)
        {
            int? replayOf = int.TryParse(replay.Log.CaptureId, NumberStyles.None, CultureInfo.InvariantCulture, out var id) ? id : null;
            return new RawCapture(RelativeRawDir(pipelineDataRoot, replay.Directory), [], null, replayOf);
        }
        return new RawCapture(RelativeRawDir(pipelineDataRoot, sink!.Directory), sink.Entries, sink.LogSha256(), null);
    }

    private static string RelativeRawDir(string pipelineDataRoot, string dir) =>
        Path.GetRelativePath(pipelineDataRoot, dir).Replace('\\', '/');

    private static void PruneRaw(string stateRawDir, int keep, TextWriter log)
    {
        try
        {
            foreach (var removed in RawRetention.Prune(stateRawDir, keep))
                log.WriteLine($"Pruned raw capture {removed} (--keep-raw {keep}).");
        }
        catch (IOException ex)
        {
            log.WriteLine($"Warning: could not prune raw captures in {stateRawDir}: {ex.Message}");
        }
    }

    /// <summary>
    /// Inputs come from --input-root, else --out, else the repo data/ folder. The output root
    /// is null unless the caller asked for files with --output-root or --out.
    /// </summary>
    private static (string PipelineDataRoot, string? OutputRoot) ResolveRoots(RunRequest request)
    {
        var pipelineDataRoot = request.InputRoot ?? request.LegacyOutRoot ?? FindDataRoot();
        var outputRoot = request.OutputRoot
            ?? (request.LegacyOutRoot is not null ? DataPaths.OutputRoot(request.LegacyOutRoot) : null);

        // A data-repo checkout passed as --input-root has no catalog; fall back to the repo's.
        try
        {
            StateCatalog.LoadFromDataRoot(pipelineDataRoot);
        }
        catch (InvalidOperationException ex)
        {
            var repoData = FindDataRoot();
            if (repoData == pipelineDataRoot)
                throw new RunSetupException(ex.Message);
            pipelineDataRoot = repoData;
        }

        return (pipelineDataRoot, outputRoot);
    }

    /// <summary>Walks up from the executable to the repo root (has src/ and data/ side by side).</summary>
    public static string FindDataRoot()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
        {
            if (Directory.Exists(Path.Combine(d.FullName, "src")) && Directory.Exists(Path.Combine(d.FullName, "data")))
                return Path.Combine(d.FullName, "data");
        }
        return Path.Combine(Environment.CurrentDirectory, "data");
    }

    private static string? TryGetGitSha(string startDir)
    {
        try
        {
            var psi = new ProcessStartInfo("git", "rev-parse HEAD")
            {
                WorkingDirectory = startDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            using var p = Process.Start(psi);
            if (p is null) return null;
            var output = p.StandardOutput.ReadToEnd().Trim();
            if (!p.WaitForExit(5000) || p.ExitCode != 0) return null;
            return output.Length == 40 ? output : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Writes to two writers; used to keep console output while capturing it.</summary>
    private sealed class TeeTextWriter(TextWriter a, TextWriter b) : TextWriter
    {
        public override System.Text.Encoding Encoding => a.Encoding;
        public override void Write(char value) { a.Write(value); b.Write(value); }
        public override void Write(string? value) { a.Write(value); b.Write(value); }
        public override void WriteLine(string? value) { a.WriteLine(value); b.WriteLine(value); }
        public override void Flush() { a.Flush(); b.Flush(); }
    }
}
