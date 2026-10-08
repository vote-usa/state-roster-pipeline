using Dapper;
using StateBallot.Core.Raw;
using Testcontainers.MySql;

namespace StateBallot.Staging.Tests;

public class PassViewTests
{
    [Fact]
    public void Sources_FlattenGroupsAndSummarizeCountyBallots()
    {
        var pass = new StagingQueries.PassView
        {
            GapsJson = """["County guide empty for Skamania."]""",
            SourcesJson =
                """
                {
                  "gaps": ["County guide empty for Skamania."],
                  "next_run": { "recommended_after": "2027-05-20", "reason": "Filing week ends in May." },
                  "elections": [{ "url": "https://example.gov/elections", "format": "html", "payload_sha256": ["abc"] }],
                  "verification_only": [{ "url": "https://example.org/check", "format": "html", "payload_sha256": [] }],
                  "county_ballots": { "King": [{ "url": "https://example.gov/king" }], "Pierce": [{ "url": "https://example.gov/pierce" }] }
                }
                """,
        };

        Assert.Equal(["County guide empty for Skamania."], pass.Gaps);
        Assert.Equal(new StagingQueries.NextRunView("2027-05-20", "Filing week ends in May."), pass.NextRun);
        Assert.Equal(
            [
                new StagingQueries.SourceView("elections", "https://example.gov/elections", "html", true),
                new StagingQueries.SourceView("verification_only", "https://example.org/check", "html", false),
                new StagingQueries.SourceView("county_ballots", "2 counties, 2 source URLs", "", true),
            ],
            pass.Sources);
    }

    [Fact]
    public void MissingJson_GivesEmptyResults()
    {
        var pass = new StagingQueries.PassView();

        Assert.Empty(pass.Gaps);
        Assert.Empty(pass.Sources);
        Assert.Null(pass.NextRun);
    }
}

/// <summary>Runs the queries against a real MySQL so the SQL itself is exercised. Needs Docker.</summary>
[Trait("Category", "Integration")]
public sealed class StagingQueriesIntegrationTests : IAsyncLifetime
{
    private readonly MySqlContainer _mysql = new MySqlBuilder("mysql:8.4")
        .WithDatabase("roster_staging")
        .WithUsername("roster")
        .WithPassword("roster")
        .Build();

    private readonly string _dataRoot = Directory.CreateTempSubdirectory("staging-queries-").FullName;
    private StagingQueries _queries = null!;

    public async Task InitializeAsync()
    {
        await _mysql.StartAsync();
        var db = new StagingDb(_mysql.GetConnectionString(), _mysql.GetConnectionString());
        await new Migrator(db.StagingConnectionString).ApplyAsync();
        _queries = new StagingQueries(db, _dataRoot);

        // Capture 1 keeps its payloads and was normalized twice. Capture 2 was pruned.
        Directory.CreateDirectory(Path.Combine(_dataRoot, "raw", "tx", "1"));
        File.WriteAllText(Path.Combine(_dataRoot, "raw", "tx", "1", RawSink.LogFileName), "{}");

        await using var cn = await db.OpenStagingAsync();
        await cn.ExecuteAsync(
            """
            INSERT INTO Captures (CaptureId, StateCode, Year, Status, StartedAt, FinishedAt, RawDir, FetchCount) VALUES
                (1, 'TX', 2026, 'succeeded', '2026-10-01 10:00:00', '2026-10-01 10:00:05', 'raw/tx/1', 1),
                (2, 'TX', 2026, 'succeeded', '2026-10-02 10:00:00', '2026-10-02 10:00:05', 'raw/tx/2', 0);
            INSERT INTO CaptureFetches (CaptureId, Seq, FetchedAt, Role, KeysJson, Method, Url, Status, DurationMs) VALUES
                (1, 1, '2026-10-01 10:00:01', 'candidates', '{"election": "53815"}', 'POST', 'https://example.gov/api', 200, 12);
            INSERT INTO CollectionPasses (PassId, CaptureId, StateCode, Year, Status, RequestedAt, GapsJson) VALUES
                (1, 1, 'TX', 2026, 'succeeded', '2026-10-01 10:00:05', '[]'),
                (2, 1, 'TX', 2026, 'succeeded', '2026-10-03 09:00:00', '["One gap."]'),
                (3, 2, 'TX', 2026, 'failed', '2026-10-04 09:00:00', NULL);
            INSERT INTO Runs (RunId, PassId, StateCode, Year, ElectionDate, ElectionType, SourceElectionId, CandidateCount, CreatedAt) VALUES
                (1, 1, 'TX', 2026, '2026-11-03', 'GE', '53815', 2, '2026-10-01 10:00:06'),
                (2, 1, 'TX', 2026, '2026-11-03', 'Special', '66618', 0, '2026-10-01 10:00:06'),
                (3, 1, 'TX', 2026, '2026-11-03', 'Special', '66734', 0, '2026-10-01 10:00:06'),
                (4, 2, 'TX', 2026, '2026-11-03', 'GE', '53815', 3, '2026-10-03 09:00:01');
            INSERT INTO RunCandidates (RunId, StateCode, ElectionDate, Office, CandidateName, Party) VALUES
                (4, 'TX', '2026-11-03', 'Governor', 'Ann Able', 'R'),
                (4, 'TX', '2026-11-03', 'Governor', 'Bo_b Baker', 'D'),
                (4, 'TX', '2026-11-03', 'Land Commissioner', 'Cy Cole', 'D');
            INSERT INTO RunCountyBallots (RunCountyBallotId, RunId, StateCode, County) VALUES
                (1, 4, 'TX', 'Bexar'), (2, 4, 'TX', 'Travis');
            INSERT INTO RunCountyBallotCandidates (RunCountyBallotId, RunId, Office, CandidateName) VALUES
                (1, 4, 'Governor', 'Ann Able'), (1, 4, 'County Judge', 'Dee Diaz'), (2, 4, 'Governor', 'Ann Able');
            INSERT INTO RunCountyBallotMeasures (RunCountyBallotId, RunId, MeasureId, Title, Jurisdiction) VALUES
                (2, 4, 'Prop A', 'Transit bond', 'City of Austin');
            """);
    }

    public async Task DisposeAsync()
    {
        Directory.Delete(_dataRoot, recursive: true);
        await _mysql.DisposeAsync();
    }

    [Fact]
    public async Task LatestRuns_GroupBySourceElectionId_AndCountHistory()
    {
        var latest = await _queries.LatestRunsAsync();

        // Two specials share a date and a type and must stay separate elections.
        Assert.Equal(new[] { (4, 2L), (2, 1L), (3, 1L) }, latest.Select(r => (r.RunId, r.RunCount)).ToArray());
        Assert.Equal("2026-11-03", latest[0].ElectionDate);
        Assert.Equal("2026-10-03T09:00:01Z", latest[0].CreatedAt);
    }

    [Fact]
    public async Task Captures_ReportWhetherPayloadsAreStillOnDisk()
    {
        var captures = await _queries.CapturesAsync("TX");

        Assert.Equal(new[] { (2, false), (1, true) }, captures.Select(c => (c.CaptureId, c.FilesPresent)).ToArray());
        Assert.Equal(2, (await _queries.LatestCapturesAsync()).Single().CaptureId);

        var fetch = Assert.Single(await _queries.FetchesAsync(1));
        Assert.Equal("53815", fetch.Keys!.Value.GetProperty("election").GetString());
    }

    [Fact]
    public async Task Passes_LatestAndLatestSucceededDiffer()
    {
        Assert.Equal(3, (await _queries.LatestPassesAsync()).Single().PassId);

        var succeeded = (await _queries.LatestSucceededPassesAsync()).Single();
        Assert.Equal(2, succeeded.PassId);
        Assert.Equal(["One gap."], succeeded.Gaps);

        Assert.Equal(new[] { 2, 1 }, (await _queries.PassesForCaptureAsync(1)).Select(p => p.PassId).ToArray());
        Assert.Equal(new[] { 4, 3, 2, 1 }, (await _queries.RunsForCaptureAsync(1)).Select(r => r.RunId).ToArray());
    }

    [Fact]
    public async Task County_ShowsThatCountysBallotInsteadOfTheRunsRows()
    {
        var bexar = await _queries.CandidatesAsync(4, null, offset: 0, limit: 10, county: "Bexar");
        Assert.Equal(new[] { ("Ann Able", "Bexar"), ("Dee Diaz", "Bexar") }, bexar.Rows.Select(c => (c.Name, c.County!)).ToArray());
        Assert.Equal(1, (await _queries.CandidatesAsync(4, "judge", offset: 0, limit: 10, county: "Bexar")).Total);

        Assert.Equal(0, (await _queries.MeasuresAsync(4, offset: 0, limit: 10, county: "Bexar")).Total);
        Assert.Equal("Prop A", (await _queries.MeasuresAsync(4, offset: 0, limit: 10, county: "Travis")).Rows.Single().MeasureId);
        Assert.Equal(0, (await _queries.MeasuresAsync(4, offset: 0, limit: 10)).Total);
    }

    [Fact]
    public async Task Candidates_PageAndSearchLiterally()
    {
        var firstPage = await _queries.CandidatesAsync(4, null, offset: 0, limit: 2);
        Assert.Equal(3, firstPage.Total);
        Assert.Equal(new[] { "Ann Able", "Bo_b Baker" }, firstPage.Rows.Select(c => c.Name).ToArray());

        var governors = await _queries.CandidatesAsync(4, "governor", offset: 0, limit: 10);
        Assert.Equal(2, governors.Total);

        // An underscore is a LIKE wildcard unless escaped. It must only match itself.
        var underscore = await _queries.CandidatesAsync(4, "o_b", offset: 0, limit: 10);
        Assert.Equal(new[] { "Bo_b Baker" }, underscore.Rows.Select(c => c.Name).ToArray());
    }
}
