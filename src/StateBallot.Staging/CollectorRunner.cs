using System.Diagnostics;
using StateBallot.Core;
using StateBallot.States.Ca;
using StateBallot.States.Tx;
using StateBallot.States.Wa;
using StateBallot.States.Wv;

namespace StateBallot.Staging;

/// <summary>
/// Executes one collector run end to end: catalog check, discovery, collect, write the
/// JSON/CSV + sources.json, and (optionally) persist the run into the staging schema.
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
                $"Implemented: {string.Join(", ", catalog.ImplementedCodes.Order())}. See logs/adding-a-state.md.");

        var collectors = CollectorDiscovery.Discover();
        if (!collectors.TryGetValue(state, out var factory))
            throw new RunSetupException(
                $"State '{state}' is marked implemented in the catalog but no [StateCode(\"{state}\")] " +
                "collector was discovered. Ensure the state project is referenced and its DLL is copied to the output directory.");

        if (request.Persist && _db is null)
            throw new RunSetupException("Persist requested but no staging database was configured.");

        var stateOutputDir = DataPaths.StateOutputDir(outputRoot, state);
        var inputDataRoot = Path.GetFullPath(pipelineDataRoot);
        var sourcesPath = DataPaths.SourcesPath(inputDataRoot, state);

        RunWriter? writer = null;
        int? runId = null;
        if (request.Persist)
        {
            writer = new RunWriter(_db!);
            runId = await writer.BeginAsync(request, TryGetGitSha(inputDataRoot), ct);
            log.WriteLine($"Staging run {runId} ({StagingDb.Describe(_db!.StagingConnectionString)})");
        }

        // Collectors write progress with Console.WriteLine. Tee the console into a buffer
        // so the run's log can be stored. Process-global: one run at a time per process.
        var captured = new StringWriter();
        var originalOut = Console.Out;
        var tee = new TeeTextWriter(log, captured);
        Console.SetOut(tee);
        try
        {
            using var fetcher = new HttpFetcher();
            if (request.Wayback is not null)
            {
                tee.WriteLine($"Wayback replay: rewriting fetches to web.archive.org captures near {request.Wayback}.");
                var stamp = request.Wayback;
                fetcher.RewriteUrl = url =>
                    url.Contains("web.archive.org", StringComparison.OrdinalIgnoreCase)
                        ? url
                        : $"https://web.archive.org/web/{stamp}id_/{url}";
            }

            var collector = factory(fetcher, request.Year, stateOutputDir, inputDataRoot);
            var result = await collector.CollectAsync();

            var summary = new StringWriter();
            result.PrintSummary(summary);
            tee.Write(summary.ToString());

            var filesWritten = false;
            if (request.DryRun)
            {
                tee.WriteLine("\nDry run - no files written.");
            }
            else
            {
                new ResultWriter(stateOutputDir, sourcesPath).WriteAll(result);
                filesWritten = true;
                tee.WriteLine($"\nOutputs written to {Path.GetFullPath(stateOutputDir)}");
                tee.WriteLine($"Sources written to {Path.GetFullPath(sourcesPath)}");
            }

            if (writer is not null)
            {
                tee.Flush();
                await writer.CompleteAsync(runId!.Value, result, summary.ToString(), captured.ToString(), ct);
                tee.WriteLine($"Run {runId} persisted to staging.");
            }

            return new RunOutcome(result, stateOutputDir, sourcesPath, filesWritten, runId);
        }
        catch (Exception ex) when (writer is not null && ex is not RunSetupException)
        {
            tee.Flush();
            await writer.FailAsync(runId!.Value, ex.ToString(), captured.ToString(), CancellationToken.None);
            throw;
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    /// <summary>Mirrors the CLI's root rules: --input-root, else --out, else the repo data/ folder.</summary>
    private static (string PipelineDataRoot, string OutputRoot) ResolveRoots(RunRequest request)
    {
        var pipelineDataRoot = request.InputRoot ?? request.LegacyOutRoot ?? FindDataRoot();
        var outputRoot = request.OutputRoot
            ?? (request.LegacyOutRoot is not null
                ? DataPaths.OutputRoot(request.LegacyOutRoot)
                : DataPaths.OutputRoot(pipelineDataRoot));

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
