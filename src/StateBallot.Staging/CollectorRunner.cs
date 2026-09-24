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
/// Executes a run end to end in two stages: capture every payload to disk (a Captures row),
/// then normalize that capture into a pass with one run per election. Either stage can run
/// alone: --capture-only stops after the first, --normalize re-runs the second on an existing
/// capture. Files are exported only when the caller names an output root.
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

        if (request.Normalize is not null && request.CaptureOnly)
            throw new RunSetupException("--normalize reads an existing capture, so it cannot be combined with --capture-only.");
        if (request.Normalize is not null && request.Wayback is not null)
            throw new RunSetupException("--normalize reads an existing capture, so it cannot be combined with --wayback.");

        var persist = request.Persist && !request.DryRun;
        if (persist && _db is null)
            throw new RunSetupException("No staging database was configured. Pass --dry-run to collect without storing.");

        var inputDataRoot = Path.GetFullPath(pipelineDataRoot);
        var stateOutputDir = outputRoot is null ? null : DataPaths.StateOutputDir(outputRoot, state);
        var stateRawDir = RawRetention.StateDir(inputDataRoot, state);
        var gitSha = TryGetGitSha(inputDataRoot);

        string captureDir;
        int? captureId;
        if (request.Normalize is { } existing)
        {
            (captureDir, captureId) = await LocateCaptureAsync(existing, state, stateRawDir, persist, ct);
            request = request with { Year = RawSink.ReadLog(captureDir).Year };
        }
        else
        {
            (captureDir, captureId) = await CaptureAsync(
                request, state, factory, stateRawDir, inputDataRoot, stateOutputDir, persist, gitSha, log, ct);
            if (request.CaptureOnly)
                return new RunOutcome(null, Path.GetFileName(captureDir), null, [], 0, false, stateOutputDir);
        }

        return await NormalizeAsync(
            request, state, factory, captureDir, captureId, inputDataRoot, stateOutputDir, persist, gitSha, log, ct);
    }

    /// <summary>
    /// Stage 1: fetch every payload the collector needs into data/raw/&lt;xx&gt;/&lt;capture-id&gt;/.
    /// Returns the capture directory and, when stored, its Captures id.
    /// </summary>
    private async Task<(string Dir, int? Id)> CaptureAsync(
        RunRequest request, string state, Func<int, string, string?, IStateCollector> factory, string stateRawDir,
        string inputDataRoot, string? stateOutputDir, bool persist, string? gitSha, TextWriter log, CancellationToken ct)
    {
        var writer = persist ? new CaptureWriter(_db!) : null;
        int? captureId = writer is null ? null : await writer.BeginAsync(request, gitSha, ct);
        var sink = StartCapture(stateRawDir, captureId, state, request.Year, log);
        var rawDir = Path.GetRelativePath(inputDataRoot, sink.Directory).Replace('\\', '/');
        log.WriteLine(captureId is null
            ? $"Dry-run capture {sink.CaptureId}, not stored"
            : $"Capture {captureId} ({StagingDb.Describe(_db!.StagingConnectionString)})");

        var captured = new StringWriter();
        try
        {
            using (ConsoleTee.Start(log, captured))
            {
                using var fetcher = new HttpFetcher { RawSink = sink };
                if (request.Wayback is not null)
                {
                    Console.WriteLine($"Wayback: rewriting fetches to web.archive.org captures near {request.Wayback}.");
                    var stamp = request.Wayback;
                    fetcher.RewriteUrl = url =>
                        url.Contains("web.archive.org", StringComparison.OrdinalIgnoreCase)
                            ? url
                            : $"https://web.archive.org/web/{stamp}id_/{url}";
                }

                var collector = factory(request.Year, stateOutputDir ?? inputDataRoot, inputDataRoot);
                await collector.CaptureAsync(fetcher, DateOnly.FromDateTime(sink.StartedAt));
            }
        }
        catch (Exception ex)
        {
            if (writer is not null)
                await writer.FinishAsync(captureId!.Value, sink, rawDir, captured.ToString(), ex.ToString(), CancellationToken.None);
            throw;
        }
        finally
        {
            PruneRaw(stateRawDir, request.KeepRaw, log);
        }

        if (writer is not null)
            await writer.FinishAsync(captureId!.Value, sink, rawDir, captured.ToString(), null, ct);
        log.WriteLine($"Captured {sink.Entries.Count} fetch(es), {sink.TotalBytes:N0} bytes in {sink.Directory}");
        return (sink.Directory, captureId);
    }

    /// <summary>Stage 2: build the result from the capture alone, then store it as a pass with one run per election.</summary>
    private async Task<RunOutcome> NormalizeAsync(
        RunRequest request, string state, Func<int, string, string?, IStateCollector> factory, string captureDir, int? captureId,
        string inputDataRoot, string? stateOutputDir, bool persist, string? gitSha, TextWriter log, CancellationToken ct)
    {
        var capture = new CaptureReader(captureDir);

        RunWriter? writer = null;
        int? passId = null;
        if (persist)
        {
            writer = new RunWriter(_db!);
            passId = await writer.BeginPassAsync(request, captureId!.Value, gitSha, ct);
            log.WriteLine($"Collection pass {passId} normalizing capture {captureId}");
        }

        // Collectors write progress with Console.WriteLine. Tee the console into a buffer
        // so the pass log can be stored. Process-global: one pass at a time per process.
        var captured = new StringWriter();
        var tee = ConsoleTee.Start(log, captured);
        try
        {
            var collector = factory(capture.Year, stateOutputDir ?? inputDataRoot, inputDataRoot);
            var collected = collector.Normalize(capture);
            collected.Sources.AttachPayloadHashes(capture.Log.Fetches);

            var result = collected;
            if (request.ElectionDate is { } wanted)
            {
                var available = CollectResultFilter.ElectionDates(collected);
                result = CollectResultFilter.ToElectionDate(collected, wanted);
                if (result.Elections.Count == 0)
                    throw new RunSetupException(
                        $"No {state} election on {wanted} for {capture.Year}. " +
                        $"Found: {(available.Count == 0 ? "none" : string.Join(", ", available))}.");
                Console.WriteLine($"Filtered to the {wanted} election.");
            }

            var summary = new StringWriter();
            result.PrintSummary(summary);
            Console.Write(summary.ToString());

            var filesWritten = false;
            if (stateOutputDir is not null && !request.DryRun)
            {
                var sourcesPath = DataPaths.SourcesPath(inputDataRoot, state);
                new ResultWriter(stateOutputDir, sourcesPath).WriteAll(result);
                filesWritten = true;
                Console.WriteLine($"\nFiles exported to {Path.GetFullPath(stateOutputDir)}");
            }

            IReadOnlyList<ElectionRun> runs = [];
            var unassigned = 0;
            if (writer is not null)
            {
                Console.Out.Flush();
                var stored = await writer.CompletePassAsync(
                    passId!.Value, result, summary.ToString(), captured.ToString(), ct);
                runs = stored.Runs;
                unassigned = stored.UnassignedRowCount;

                Console.WriteLine($"\nPass {passId} stored {runs.Count} election run(s):");
                foreach (var run in runs)
                {
                    var pending = run.IsPending ? ", pending at the source" : "";
                    Console.WriteLine(
                        $"  run {run.RunId}  {run.ElectionDate}  {run.ElectionType,-16} " +
                        $"{run.CandidateCount} candidate(s), {run.MeasureCount} measure(s), " +
                        $"{run.CountyBallotCount} county ballot(s){pending}");
                }
                if (unassigned > 0)
                    Console.WriteLine($"  {unassigned} row(s) could not be matched to an election, see PassUnassignedRows.");
            }
            else if (request.DryRun)
            {
                Console.WriteLine("\nDry run - nothing stored.");
            }

            return new RunOutcome(result, capture.CaptureId, passId, runs, unassigned, filesWritten, stateOutputDir);
        }
        catch (Exception ex) when (writer is not null)
        {
            Console.WriteLine(
                $"\nNormalize failed. Capture {capture.CaptureId} is kept: fix the parser and re-run " +
                $"--normalize {capture.CaptureId} --state {state}{(captureId is null ? " --dry-run" : "")}.");
            Console.Out.Flush();
            if (writer is not null)
                await writer.FailPassAsync(passId!.Value, ex.ToString(), captured.ToString(), CancellationToken.None);
            throw;
        }
        finally
        {
            tee.Dispose();
        }
    }

    /// <summary>
    /// Finds an existing capture to normalize. A numbered capture must be a succeeded Captures
    /// row for this state when the pass will be stored. A dry-run capture is not in the
    /// database, so it can only be normalized with --dry-run.
    /// </summary>
    private async Task<(string Dir, int? Id)> LocateCaptureAsync(
        string captureName, string state, string stateRawDir, bool persist, CancellationToken ct)
    {
        int? captureId = null;
        if (captureName.StartsWith("dry-", StringComparison.Ordinal))
        {
            if (persist)
                throw new RunSetupException(
                    $"Capture {captureName} is a dry-run capture and is not recorded in staging. Normalize it with --dry-run.");
        }
        else if (int.TryParse(captureName, NumberStyles.None, CultureInfo.InvariantCulture, out var id))
        {
            captureId = id;
            if (persist)
            {
                var info = await new CaptureWriter(_db!).GetAsync(id, ct)
                    ?? throw new RunSetupException($"No capture {id} in staging.");
                if (!string.Equals(info.StateCode, state, StringComparison.OrdinalIgnoreCase))
                    throw new RunSetupException($"Capture {id} is for {info.StateCode}, not {state}.");
                if (info.Status != "succeeded")
                    throw new RunSetupException($"Capture {id} is {info.Status}. Only a succeeded capture can be normalized.");
            }
        }
        else
        {
            throw new RunSetupException($"--normalize takes a capture id or a dry-... capture name, got '{captureName}'.");
        }

        var dir = Path.Combine(stateRawDir, captureName);
        if (!File.Exists(Path.Combine(dir, RawSink.LogFileName)))
            throw new RunSetupException(
                $"No capture files at {dir}. They may have been pruned by --keep-raw, " +
                "in which case CaptureFetches still holds the hashes but the payloads are gone.");
        var logState = RawSink.ReadLog(dir).State;
        if (!string.Equals(logState, state, StringComparison.OrdinalIgnoreCase))
            throw new RunSetupException($"Capture {captureName} is for {logState}, not {state}.");
        return (dir, captureId);
    }

    /// <summary>
    /// Opens data/raw/&lt;xx&gt;/&lt;capture-id&gt;/, or dry-&lt;utc stamp&gt; when nothing is stored.
    /// A directory left over from a database that was since recreated is moved aside, not overwritten.
    /// </summary>
    private static RawSink StartCapture(string stateRawDir, int? captureId, string state, int year, TextWriter log)
    {
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
        var name = captureId?.ToString(CultureInfo.InvariantCulture) ?? $"dry-{stamp}";
        var dir = Path.Combine(stateRawDir, name);
        if (Directory.Exists(dir))
        {
            var aside = $"{dir}-stale-{stamp}";
            Directory.Move(dir, aside);
            log.WriteLine($"Moved an older capture that reused this capture id to {aside}.");
        }
        return new RawSink(dir, name, state, year);
    }

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

    /// <summary>Points Console.Out at both the log and a buffer until disposed.</summary>
    private sealed class ConsoleTee : IDisposable
    {
        private readonly TextWriter _original;

        private ConsoleTee(TextWriter log, TextWriter buffer)
        {
            _original = Console.Out;
            Console.SetOut(new TeeTextWriter(log, buffer));
        }

        public static ConsoleTee Start(TextWriter log, TextWriter buffer) => new(log, buffer);

        public void Dispose()
        {
            Console.Out.Flush();
            Console.SetOut(_original);
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
