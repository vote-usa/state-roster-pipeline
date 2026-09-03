using System.Text.Json;
using Dapper;
using StateBallot.Core;
using StateBallot.Core.Output;

namespace StateBallot.Staging;

/// <summary>
/// Persists a collector run into roster_staging. A run is begun (Runs row, status
/// running), then either completed (child rows + counts, status succeeded) or failed.
/// Child rows are the same shapes ResultWriter emits to JSON/CSV.
/// </summary>
public sealed class RunWriter
{
    private readonly StagingDb _db;

    public RunWriter(StagingDb db) => _db = db;

    /// <summary>Inserts a running Runs row, or marks the console-queued row as running.</summary>
    public async Task<int> BeginAsync(RunRequest request, string? gitSha, CancellationToken ct = default)
    {
        await using var cn = await _db.OpenStagingAsync(ct);
        var args = new
        {
            StateCode = request.State.ToUpperInvariant(),
            request.Year,
            request.Source,
            request.RequestedBy,
            request.CliArgs,
            GitSha = gitSha,
            request.Wayback,
        };

        if (request.ExistingRunId is { } existing)
        {
            var updated = await cn.ExecuteAsync(new CommandDefinition(
                """
                UPDATE Runs SET Status = 'running', StartedAt = UTC_TIMESTAMP(), CliArgs = @CliArgs,
                    GitSha = @GitSha, Wayback = @Wayback
                WHERE RunId = @RunId
                """,
                new { RunId = existing, args.CliArgs, args.GitSha, args.Wayback },
                cancellationToken: ct));
            if (updated == 0)
                throw new InvalidOperationException($"Run {existing} does not exist in staging.");
            return existing;
        }

        return await cn.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            INSERT INTO Runs (StateCode, Year, Status, Source, RequestedBy, RequestedAt, StartedAt, CliArgs, GitSha, Wayback)
            VALUES (@StateCode, @Year, 'running', @Source, @RequestedBy, UTC_TIMESTAMP(), UTC_TIMESTAMP(), @CliArgs, @GitSha, @Wayback);
            SELECT LAST_INSERT_ID();
            """,
            args,
            cancellationToken: ct));
    }

    public async Task CompleteAsync(int runId, CollectResult result, string summary, string logText, CancellationToken ct = default)
    {
        var elections = result.Elections.Select(ResultWriter.ToElectionOut).ToList();
        var pendingIds = result.PendingElections.Select(p => p.ElectionId).ToHashSet(StringComparer.Ordinal);

        await using var cn = await _db.OpenStagingAsync(ct);
        await using var tx = await cn.BeginTransactionAsync(ct);

        // Elections first so children can point at them.
        var electionIds = new List<(ElectionOut Election, int RunElectionId)>();
        foreach (var e in elections)
        {
            var id = await cn.ExecuteScalarAsync<int>(new CommandDefinition(
                """
                INSERT INTO RunElections (RunId, StateCode, ElectionDate, ElectionType, Jurisdiction, OcdDivisionId, Name,
                    SourceElectionId, SourceUrl, IsPending)
                VALUES (@RunId, @State, @ElectionDate, @ElectionType, @Jurisdiction, @OcdDivisionId, @Name,
                    @ElectionId, @SourceUrl, @IsPending);
                SELECT LAST_INSERT_ID();
                """,
                new
                {
                    RunId = runId, e.State, e.ElectionDate, e.ElectionType, e.Jurisdiction, e.OcdDivisionId, e.Name,
                    e.ElectionId, e.SourceUrl, IsPending = pendingIds.Contains(e.ElectionId),
                },
                transaction: tx, cancellationToken: ct));
            electionIds.Add((e, id));
        }

        var candidates = result.Candidates.Select(ResultWriter.ToCandidateOut).Select(c => new
        {
            RunId = runId,
            RunElectionId = LinkElection(electionIds, c.ElectionDate, c.ElectionType),
            c.State, c.ElectionDate, c.ElectionType, c.Office, c.District, c.County, c.OcdDivisionId, c.CandidateName,
            c.Party, c.Incumbent, c.SourceUrl, c.SourceCandidateId, c.FilingDate, c.Email, c.Phone, c.CampaignPhone,
            c.Website, c.Occupation, c.MailingAddressLine, c.MailingCity, c.MailingState, c.MailingZip,
            c.ResidentialCity, c.ResidentialCounty, c.SourceOfficeId, c.SourceOfficeType, c.LocalJurisdiction,
            c.FirstName, c.MiddleName, c.LastName, c.Suffix,
        }).ToList();

        await cn.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO RunCandidates (RunId, RunElectionId, StateCode, ElectionDate, ElectionType, Office, District, County,
                OcdDivisionId, CandidateName, Party, Incumbent, SourceUrl, SourceCandidateId, FilingDate, Email, Phone,
                CampaignPhone, Website, Occupation, MailingAddressLine, MailingCity, MailingState, MailingZip,
                ResidentialCity, ResidentialCounty, SourceOfficeId, SourceOfficeType, LocalJurisdiction,
                FirstName, MiddleName, LastName, Suffix)
            VALUES (@RunId, @RunElectionId, @State, @ElectionDate, @ElectionType, @Office, @District, @County,
                @OcdDivisionId, @CandidateName, @Party, @Incumbent, @SourceUrl, @SourceCandidateId, @FilingDate, @Email, @Phone,
                @CampaignPhone, @Website, @Occupation, @MailingAddressLine, @MailingCity, @MailingState, @MailingZip,
                @ResidentialCity, @ResidentialCounty, @SourceOfficeId, @SourceOfficeType, @LocalJurisdiction,
                @FirstName, @MiddleName, @LastName, @Suffix)
            """,
            candidates, transaction: tx, cancellationToken: ct));

        var measures = result.StatewideProposedMeasures.Select(m => (Row: ResultWriter.ToMeasureOut(m), Statewide: true))
            .Concat(result.Measures.Select(m => (Row: ResultWriter.ToMeasureOut(m), Statewide: false)))
            .Select(x => new
            {
                RunId = runId,
                RunElectionId = x.Row.ElectionDate is null ? null : LinkElection(electionIds, x.Row.ElectionDate, null),
                x.Row.State, x.Row.ElectionDate, x.Row.MeasureId, x.Row.Title, x.Row.Summary, x.Row.FullTextUrl,
                x.Row.Jurisdiction, x.Row.County, x.Row.OcdDivisionId, x.Row.SourceUrl, IsStatewideProposed = x.Statewide,
            }).ToList();

        await cn.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO RunMeasures (RunId, RunElectionId, StateCode, ElectionDate, MeasureId, Title, Summary, FullTextUrl,
                Jurisdiction, County, OcdDivisionId, SourceUrl, IsStatewideProposed)
            VALUES (@RunId, @RunElectionId, @State, @ElectionDate, @MeasureId, @Title, @Summary, @FullTextUrl,
                @Jurisdiction, @County, @OcdDivisionId, @SourceUrl, @IsStatewideProposed)
            """,
            measures, transaction: tx, cancellationToken: ct));

        await cn.ExecuteAsync(new CommandDefinition(
            """
            UPDATE Runs SET Status = 'succeeded', FinishedAt = UTC_TIMESTAMP(),
                ElectionCount = @ElectionCount, PendingElectionCount = @PendingElectionCount,
                CandidateCount = @CandidateCount, MeasureCount = @MeasureCount, CountyBallotCount = @CountyBallotCount,
                GapCount = @GapCount, GapsJson = @GapsJson, Summary = @Summary, LogText = @LogText, ErrorText = NULL
            WHERE RunId = @RunId
            """,
            new
            {
                RunId = runId,
                ElectionCount = result.Elections.Count,
                PendingElectionCount = result.PendingElections.Count,
                CandidateCount = result.Candidates.Count,
                MeasureCount = measures.Count,
                CountyBallotCount = result.CountyBallots.Count,
                GapCount = result.Gaps.Count,
                GapsJson = JsonSerializer.Serialize(result.Gaps),
                Summary = summary,
                LogText = logText,
            },
            transaction: tx, cancellationToken: ct));

        await tx.CommitAsync(ct);
    }

    public async Task FailAsync(int runId, string error, string logText, CancellationToken ct = default)
    {
        await using var cn = await _db.OpenStagingAsync(ct);
        await cn.ExecuteAsync(new CommandDefinition(
            "UPDATE Runs SET Status = 'failed', FinishedAt = UTC_TIMESTAMP(), ErrorText = @Error, LogText = @LogText WHERE RunId = @RunId",
            new { RunId = runId, Error = error, LogText = logText },
            cancellationToken: ct));
    }

    /// <summary>
    /// Candidate/measure rows carry election date and type but no election id. Link to the
    /// run's election when exactly one matches (date + type, or date alone when type is
    /// not given); otherwise leave null rather than guess.
    /// </summary>
    public static int? LinkElection(
        IReadOnlyList<(ElectionOut Election, int RunElectionId)> elections, string electionDate, string? electionType)
    {
        var byDate = elections.Where(x => string.Equals(x.Election.ElectionDate, electionDate, StringComparison.Ordinal)).ToList();
        if (electionType is not null)
        {
            var byType = byDate.Where(x => string.Equals(x.Election.ElectionType, electionType, StringComparison.OrdinalIgnoreCase)).ToList();
            if (byType.Count == 1) return byType[0].RunElectionId;
            if (byType.Count > 1) return null;
        }
        return byDate.Count == 1 ? byDate[0].RunElectionId : null;
    }
}
