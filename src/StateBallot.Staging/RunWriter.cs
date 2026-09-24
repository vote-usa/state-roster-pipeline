using System.Globalization;
using System.Text.Json;
using Dapper;
using StateBallot.Core;
using StateBallot.Core.Output;

namespace StateBallot.Staging;

/// <summary>
/// Persists a collection pass into roster_staging. One CLI invocation is one pass, because
/// the sources publish a whole state and year in one fetch. The pass fans out into one
/// Runs row per election, which is the unit that gets reviewed, resolved and loaded.
/// State-level artifacts that belong to no election hang off the pass.
/// </summary>
public sealed class RunWriter
{
    private readonly StagingDb _db;

    public RunWriter(StagingDb db) => _db = db;

    /// <summary>Inserts a running pass, or marks the console-queued pass as running.</summary>
    /// <param name="captureId">The capture this pass normalizes.</param>
    public async Task<int> BeginPassAsync(RunRequest request, int captureId, string? gitSha, CancellationToken ct = default)
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
            ElectionFilter = request.ElectionDate,
            CaptureId = captureId,
        };

        if (request.ExistingPassId is { } existing)
        {
            var updated = await cn.ExecuteAsync(new CommandDefinition(
                """
                UPDATE CollectionPasses SET Status = 'running', StartedAt = UTC_TIMESTAMP(), CliArgs = @CliArgs,
                    GitSha = @GitSha, Wayback = @Wayback, ElectionFilter = @ElectionFilter, CaptureId = @CaptureId
                WHERE PassId = @PassId
                """,
                new { PassId = existing, args.CliArgs, args.GitSha, args.Wayback, args.ElectionFilter, args.CaptureId },
                cancellationToken: ct));
            if (updated == 0)
                throw new InvalidOperationException($"Pass {existing} does not exist in staging.");
            return existing;
        }

        return await cn.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            INSERT INTO CollectionPasses
                (StateCode, Year, Status, Source, RequestedBy, RequestedAt, StartedAt, CliArgs, GitSha, Wayback, ElectionFilter, CaptureId)
            VALUES
                (@StateCode, @Year, 'running', @Source, @RequestedBy, UTC_TIMESTAMP(), UTC_TIMESTAMP(), @CliArgs, @GitSha, @Wayback, @ElectionFilter, @CaptureId);
            SELECT LAST_INSERT_ID();
            """,
            args,
            cancellationToken: ct));
    }

    public sealed record PassResult(IReadOnlyList<ElectionRun> Runs, int UnassignedRowCount);

    public async Task<PassResult> CompletePassAsync(
        int passId, CollectResult result, string summary, string logText, CancellationToken ct = default)
    {
        var elections = result.Elections.Select(ResultWriter.ToElectionOut).ToList();
        var pendingIds = result.PendingElections.Select(p => p.ElectionId).ToHashSet(StringComparer.Ordinal);

        var candidates = ElectionMatcher.Group(
            elections,
            result.Candidates.Select(ResultWriter.ToCandidateOut),
            c => (c.SourceElectionId, (string?)c.ElectionDate, (string?)c.ElectionType));

        var datedMeasures = result.Measures.Select(m => (Row: ResultWriter.ToMeasureOut(m), Statewide: false))
            .Concat(result.StatewideProposedMeasures
                .Where(m => m.ElectionDate is not null)
                .Select(m => (Row: ResultWriter.ToMeasureOut(m), Statewide: true)))
            .ToList();
        var measures = ElectionMatcher.Group(
            elections, datedMeasures, m => (m.Row.SourceElectionId, m.Row.ElectionDate, (string?)null));

        var ballots = ElectionMatcher.Group(
            elections,
            result.CountyBallots.Select(ResultWriter.ToBallotOut),
            b => ((string?)null, (string?)b.ElectionDate, (string?)b.ElectionType));

        var undatedProposed = result.StatewideProposedMeasures
            .Where(m => m.ElectionDate is null)
            .Select(ResultWriter.ToMeasureOut)
            .ToList();
        var directory = result.CountyDirectory.Select(ResultWriter.ToDirectoryOut).ToList();

        var runs = new List<ElectionRun>();

        await using var cn = await _db.OpenStagingAsync(ct);
        await using var tx = await cn.BeginTransactionAsync(ct);

        for (var i = 0; i < elections.Count; i++)
        {
            var election = elections[i];
            var electionCandidates = candidates.ByElection.GetValueOrDefault(i) ?? [];
            var electionMeasures = measures.ByElection.GetValueOrDefault(i) ?? [];
            var electionBallots = ballots.ByElection.GetValueOrDefault(i) ?? [];
            var isPending = pendingIds.Contains(election.ElectionId);

            var runId = await cn.ExecuteScalarAsync<int>(new CommandDefinition(
                """
                INSERT INTO Runs (PassId, StateCode, Year, ElectionDate, ElectionType, ElectionName, SourceElectionId,
                    Jurisdiction, OcdDivisionId, SourceUrl, Status, IsPending,
                    CandidateCount, MeasureCount, CountyBallotCount, CreatedAt)
                VALUES (@PassId, @State, @Year, @ElectionDate, @ElectionType, @Name, @ElectionId,
                    @Jurisdiction, @OcdDivisionId, @SourceUrl, @Status, @IsPending,
                    @CandidateCount, @MeasureCount, @CountyBallotCount, UTC_TIMESTAMP());
                SELECT LAST_INSERT_ID();
                """,
                new
                {
                    PassId = passId,
                    election.State,
                    Year = YearOf(election.ElectionDate),
                    election.ElectionDate,
                    election.ElectionType,
                    election.Name,
                    election.ElectionId,
                    election.Jurisdiction,
                    election.OcdDivisionId,
                    election.SourceUrl,
                    Status = isPending ? "pending" : "succeeded",
                    IsPending = isPending,
                    CandidateCount = electionCandidates.Count,
                    MeasureCount = electionMeasures.Count,
                    CountyBallotCount = electionBallots.Count,
                },
                transaction: tx, cancellationToken: ct));

            await InsertCandidatesAsync(cn, tx, runId, electionCandidates, ct);
            await InsertMeasuresAsync(cn, tx, runId, electionMeasures, ct);
            await InsertCountyBallotsAsync(cn, tx, runId, electionBallots, ct);

            runs.Add(new ElectionRun(
                runId, election.ElectionDate, election.ElectionType, election.Name, isPending,
                electionCandidates.Count, electionMeasures.Count, electionBallots.Count));
        }

        if (directory.Count > 0)
        {
            await cn.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO PassCountyDirectory (PassId, StateCode, CountyName, CountyFips, OcdDivisionId,
                    ElectionsOfficeUrl, Address, Phone)
                VALUES (@PassId, @State, @CountyName, @CountyFips, @OcdDivisionId, @ElectionsOfficeUrl, @Address, @Phone)
                """,
                directory.Select(d => new
                {
                    PassId = passId, d.State, d.CountyName, d.CountyFips, d.OcdDivisionId,
                    d.ElectionsOfficeUrl, d.Address, d.Phone,
                }).ToList(),
                transaction: tx, cancellationToken: ct));
        }

        if (undatedProposed.Count > 0)
        {
            await cn.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO PassProposedMeasures (PassId, StateCode, MeasureId, Title, Summary, FullTextUrl,
                    Jurisdiction, County, OcdDivisionId, SourceUrl)
                VALUES (@PassId, @State, @MeasureId, @Title, @Summary, @FullTextUrl,
                    @Jurisdiction, @County, @OcdDivisionId, @SourceUrl)
                """,
                undatedProposed.Select(m => new
                {
                    PassId = passId, m.State, m.MeasureId, m.Title, m.Summary, m.FullTextUrl,
                    m.Jurisdiction, m.County, m.OcdDivisionId, m.SourceUrl,
                }).ToList(),
                transaction: tx, cancellationToken: ct));
        }

        var unassigned = new List<object>();
        unassigned.AddRange(candidates.Unassigned.Select(u => Unassigned(
            passId, "candidate", u.Row.ElectionDate, u.Row.ElectionType, u.Reason, u.Row)));
        unassigned.AddRange(measures.Unassigned.Select(u => Unassigned(
            passId, "measure", u.Row.Row.ElectionDate, null, u.Reason, u.Row.Row)));
        unassigned.AddRange(ballots.Unassigned.Select(u => Unassigned(
            passId, "county_ballot", u.Row.ElectionDate, u.Row.ElectionType, u.Reason, u.Row)));

        if (unassigned.Count > 0)
        {
            await cn.ExecuteAsync(new CommandDefinition(
                """
                INSERT INTO PassUnassignedRows (PassId, RowType, ElectionDate, ElectionType, Reason, RowJson)
                VALUES (@PassId, @RowType, @ElectionDate, @ElectionType, @Reason, @RowJson)
                """,
                unassigned, transaction: tx, cancellationToken: ct));
        }

        await cn.ExecuteAsync(new CommandDefinition(
            """
            UPDATE CollectionPasses SET Status = 'succeeded', FinishedAt = UTC_TIMESTAMP(),
                RunCount = @RunCount, CandidateCount = @CandidateCount, MeasureCount = @MeasureCount,
                CountyBallotCount = @CountyBallotCount, CountyDirectoryCount = @CountyDirectoryCount,
                ProposedMeasureCount = @ProposedMeasureCount, UnassignedRowCount = @UnassignedRowCount,
                GapCount = @GapCount, GapsJson = @GapsJson, SourcesJson = @SourcesJson,
                Summary = @Summary, LogText = @LogText, ErrorText = NULL
            WHERE PassId = @PassId
            """,
            new
            {
                PassId = passId,
                RunCount = runs.Count,
                CandidateCount = runs.Sum(r => r.CandidateCount),
                MeasureCount = runs.Sum(r => r.MeasureCount),
                CountyBallotCount = runs.Sum(r => r.CountyBallotCount),
                CountyDirectoryCount = directory.Count,
                ProposedMeasureCount = undatedProposed.Count,
                UnassignedRowCount = unassigned.Count,
                GapCount = result.Gaps.Count,
                GapsJson = JsonSerializer.Serialize(result.Gaps),
                SourcesJson = JsonSerializer.Serialize(
                    result.Sources.ToJsonObject(result.Gaps), OutputWriter.JsonOptions),
                Summary = summary,
                LogText = logText,
            },
            transaction: tx, cancellationToken: ct));

        await tx.CommitAsync(ct);
        return new PassResult(runs, unassigned.Count);
    }

    public async Task FailPassAsync(int passId, string error, string logText, CancellationToken ct = default)
    {
        await using var cn = await _db.OpenStagingAsync(ct);
        await cn.ExecuteAsync(new CommandDefinition(
            """
            UPDATE CollectionPasses SET Status = 'failed', FinishedAt = UTC_TIMESTAMP(),
                ErrorText = @Error, LogText = @LogText
            WHERE PassId = @PassId
            """,
            new { PassId = passId, Error = error, LogText = logText },
            cancellationToken: ct));
    }

    private static async Task InsertCandidatesAsync(
        System.Data.Common.DbConnection cn, System.Data.Common.DbTransaction tx,
        int runId, List<CandidateOut> rows, CancellationToken ct)
    {
        if (rows.Count == 0)
            return;

        await cn.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO RunCandidates (RunId, StateCode, ElectionDate, ElectionType, Office, District, County,
                OcdDivisionId, CandidateName, Party, Incumbent, SourceUrl, SourceCandidateId, SourceElectionId, FilingDate, Email, Phone,
                CampaignPhone, Website, Occupation, MailingAddressLine, MailingCity, MailingState, MailingZip,
                ResidentialCity, ResidentialCounty, SourceOfficeId, SourceOfficeType, LocalJurisdiction,
                FirstName, MiddleName, LastName, Suffix)
            VALUES (@RunId, @State, @ElectionDate, @ElectionType, @Office, @District, @County,
                @OcdDivisionId, @CandidateName, @Party, @Incumbent, @SourceUrl, @SourceCandidateId, @SourceElectionId, @FilingDate, @Email, @Phone,
                @CampaignPhone, @Website, @Occupation, @MailingAddressLine, @MailingCity, @MailingState, @MailingZip,
                @ResidentialCity, @ResidentialCounty, @SourceOfficeId, @SourceOfficeType, @LocalJurisdiction,
                @FirstName, @MiddleName, @LastName, @Suffix)
            """,
            rows.Select(c => new
            {
                RunId = runId,
                c.State, c.ElectionDate, c.ElectionType, c.Office, c.District, c.County, c.OcdDivisionId,
                c.CandidateName, c.Party, c.Incumbent, c.SourceUrl, c.SourceCandidateId, c.SourceElectionId, c.FilingDate,
                c.Email, c.Phone, c.CampaignPhone, c.Website, c.Occupation,
                c.MailingAddressLine, c.MailingCity, c.MailingState, c.MailingZip,
                c.ResidentialCity, c.ResidentialCounty, c.SourceOfficeId, c.SourceOfficeType, c.LocalJurisdiction,
                c.FirstName, c.MiddleName, c.LastName, c.Suffix,
            }).ToList(),
            transaction: tx, cancellationToken: ct));
    }

    private static async Task InsertMeasuresAsync(
        System.Data.Common.DbConnection cn, System.Data.Common.DbTransaction tx,
        int runId, List<(MeasureOut Row, bool Statewide)> rows, CancellationToken ct)
    {
        if (rows.Count == 0)
            return;

        await cn.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO RunMeasures (RunId, StateCode, ElectionDate, SourceElectionId, MeasureId, Title, Summary,
                FullTextUrl, Jurisdiction, County, OcdDivisionId, SourceUrl, IsStatewideProposed)
            VALUES (@RunId, @State, @ElectionDate, @SourceElectionId, @MeasureId, @Title, @Summary,
                @FullTextUrl, @Jurisdiction, @County, @OcdDivisionId, @SourceUrl, @IsStatewideProposed)
            """,
            rows.Select(x => new
            {
                RunId = runId,
                x.Row.State, x.Row.ElectionDate, x.Row.SourceElectionId, x.Row.MeasureId, x.Row.Title, x.Row.Summary, x.Row.FullTextUrl,
                x.Row.Jurisdiction, x.Row.County, x.Row.OcdDivisionId, x.Row.SourceUrl,
                IsStatewideProposed = x.Statewide,
            }).ToList(),
            transaction: tx, cancellationToken: ct));
    }

    private static async Task InsertCountyBallotsAsync(
        System.Data.Common.DbConnection cn, System.Data.Common.DbTransaction tx,
        int runId, List<CountyBallotOut> ballots, CancellationToken ct)
    {
        foreach (var ballot in ballots)
        {
            var ballotId = await cn.ExecuteScalarAsync<int>(new CommandDefinition(
                """
                INSERT INTO RunCountyBallots (RunId, StateCode, County, OcdDivisionId, SourceUrl,
                    CandidateCount, MeasureCount)
                VALUES (@RunId, @State, @County, @OcdDivisionId, @SourceUrl, @CandidateCount, @MeasureCount);
                SELECT LAST_INSERT_ID();
                """,
                new
                {
                    RunId = runId, ballot.State, ballot.County, ballot.OcdDivisionId, ballot.SourceUrl,
                    CandidateCount = ballot.Candidates.Count, MeasureCount = ballot.Measures.Count,
                },
                transaction: tx, cancellationToken: ct));

            if (ballot.Candidates.Count > 0)
            {
                await cn.ExecuteAsync(new CommandDefinition(
                    """
                    INSERT INTO RunCountyBallotCandidates (RunCountyBallotId, RunId, Office, District, OcdDivisionId,
                        CandidateName, Party, SourceOfficeId, SourceOfficeType, LocalJurisdiction)
                    VALUES (@RunCountyBallotId, @RunId, @Office, @District, @OcdDivisionId,
                        @CandidateName, @Party, @SourceOfficeId, @SourceOfficeType, @LocalJurisdiction)
                    """,
                    ballot.Candidates.Select(c => new
                    {
                        RunCountyBallotId = ballotId, RunId = runId,
                        c.Office, c.District, c.OcdDivisionId, c.CandidateName, c.Party,
                        c.SourceOfficeId, c.SourceOfficeType, c.LocalJurisdiction,
                    }).ToList(),
                    transaction: tx, cancellationToken: ct));
            }

            if (ballot.Measures.Count > 0)
            {
                await cn.ExecuteAsync(new CommandDefinition(
                    """
                    INSERT INTO RunCountyBallotMeasures (RunCountyBallotId, RunId, MeasureId, Title, Summary,
                        Jurisdiction, OcdDivisionId)
                    VALUES (@RunCountyBallotId, @RunId, @MeasureId, @Title, @Summary, @Jurisdiction, @OcdDivisionId)
                    """,
                    ballot.Measures.Select(m => new
                    {
                        RunCountyBallotId = ballotId, RunId = runId,
                        m.MeasureId, m.Title, m.Summary, m.Jurisdiction, m.OcdDivisionId,
                    }).ToList(),
                    transaction: tx, cancellationToken: ct));
            }
        }
    }

    private static object Unassigned<T>(
        int passId, string rowType, string? electionDate, string? electionType, string reason, T row) => new
    {
        PassId = passId,
        RowType = rowType,
        ElectionDate = electionDate,
        ElectionType = electionType,
        Reason = reason,
        RowJson = JsonSerializer.Serialize(row, OutputWriter.JsonOptions),
    };

    private static int YearOf(string electionDate) =>
        int.Parse(electionDate.AsSpan(0, 4), CultureInfo.InvariantCulture);
}
