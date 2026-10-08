using System.Globalization;
using StateBallot.Staging;

namespace StateBallot.Api;

/// <summary>Where the pipeline's data/ folder is, for the catalog, input files and raw captures.</summary>
public sealed record DataRoot(string Path);

/// <summary>
/// Runs queued jobs through CollectorRunner, one at a time. One at a time is required, not a
/// choice: CollectorRunner redirects the process-wide console to capture a run's log.
/// </summary>
public sealed class JobWorker(StagingDb db, JobStore jobs, DataRoot dataRoot, ILogger<JobWorker> logger) : BackgroundService
{
    private static readonly TimeSpan Idle = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan AfterError = TimeSpan.FromSeconds(10);

    protected override async Task ExecuteAsync(CancellationToken stopping)
    {
        var recovered = false;
        while (!stopping.IsCancellationRequested)
        {
            try
            {
                if (!recovered)
                {
                    var interrupted = await jobs.FailInterruptedAsync(stopping);
                    if (interrupted > 0)
                        logger.LogWarning("Marked {Count} job(s) failed that were running when the console last stopped.", interrupted);
                    recovered = true;
                }

                if (await jobs.ClaimNextAsync(stopping) is { } job)
                    await RunAsync(job, stopping);
                else
                    await Task.Delay(Idle, stopping);
            }
            catch (OperationCanceledException) when (stopping.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(
                    "The job queue could not be read: {Message} If the Jobs table is missing, run the CLI with --migrate.", ex.Message);
                await Task.Delay(AfterError, stopping).ContinueWith(_ => { }, CancellationToken.None);
            }
        }
    }

    private async Task RunAsync(JobStore.JobView job, CancellationToken stopping)
    {
        logger.LogInformation("Job {JobId}: {Kind} for {State} {Year}", job.JobId, job.Kind, job.StateCode, job.Year);
        var request = new RunRequest(job.StateCode, job.Year)
        {
            InputRoot = dataRoot.Path,
            ElectionDate = job.ElectionFilter,
            Normalize = job.Kind == JobStore.Normalize ? job.NormalizeCaptureId?.ToString(CultureInfo.InvariantCulture) : null,
            CaptureOnly = job.Kind == JobStore.Capture,
            RequestedBy = job.RequestedBy,
            Source = "web",
            CliArgs = $"console job {job.JobId}",
        };

        try
        {
            var outcome = await new CollectorRunner(db).RunAsync(request, Console.Out, stopping);
            var captureId = int.TryParse(outcome.CaptureId, NumberStyles.None, CultureInfo.InvariantCulture, out var id) ? id : (int?)null;
            await jobs.SucceedAsync(job.JobId, captureId, outcome.PassId, CancellationToken.None);
            logger.LogInformation("Job {JobId} succeeded: capture {CaptureId}, pass {PassId}", job.JobId, captureId, outcome.PassId);
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested)
        {
            // Left running on purpose. The next start fails it with the reason.
            throw;
        }
        catch (Exception ex)
        {
            // The full error is on the capture or pass row. The job keeps the message.
            await jobs.FailAsync(job, ex.Message, CancellationToken.None);
            logger.LogWarning("Job {JobId} failed: {Message}", job.JobId, ex.Message);
        }
    }
}
