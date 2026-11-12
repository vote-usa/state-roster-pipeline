using Dapper;
using Testcontainers.MySql;

namespace StateBallot.Staging.Tests;

/// <summary>Region URLs and their versions against a real MySQL. Needs Docker.</summary>
[Trait("Category", "Integration")]
public sealed class RegionUrlStoreTests : IAsyncLifetime
{
    private const string Candidates = "https://www.sos.arkansas.gov/candidates.csv";

    private readonly MySqlContainer _mysql = new MySqlBuilder("mysql:8.4")
        .WithDatabase("roster_staging")
        .WithUsername("roster")
        .WithPassword("roster")
        .Build();

    private StagingDb _db = null!;
    private RegionUrlStore _urls = null!;

    public async Task InitializeAsync()
    {
        await _mysql.StartAsync();
        _db = new StagingDb(_mysql.GetConnectionString(), _mysql.GetConnectionString());
        await new Migrator(_db.StagingConnectionString).ApplyAsync();
        _urls = new RegionUrlStore(_db);
    }

    public Task DisposeAsync() => _mysql.DisposeAsync().AsTask();

    private static RegionUrlStore.SaveUrl Url(
        string url = Candidates, string type = "candidate_data", string[]? counties = null, string? city = null, string state = "AR") =>
        RegionUrlStore.Clean(new(state, counties ?? [], city, url, type, "tester"));

    [Fact]
    public async Task Add_ListsTheUrl_AndRecordsAVersion()
    {
        var added = await _urls.AddAsync(Url(counties: ["Boone", "Bradley"], city: "Little Rock"));

        var listed = Assert.Single(await _urls.ListAsync("AR"));
        Assert.Equal((added.UrlId, Candidates, "candidate_data", "Little Rock", "tester"), (listed.UrlId, listed.Url, listed.UrlType, listed.City, listed.UpdatedBy));
        Assert.Equal(["Boone", "Bradley"], listed.Counties);
        Assert.Empty(await _urls.ListAsync("TX"));

        var version = Assert.Single(await _urls.HistoryAsync(added.UrlId));
        Assert.Equal((RegionUrlStore.Added, "Little Rock"), (version.Action, version.City));
    }

    [Fact]
    public async Task Add_RefusesTheSameUrlForTheSameRegionAndType()
    {
        await _urls.AddAsync(Url(counties: ["Boone", "Bradley"]));

        await Assert.ThrowsAsync<ArgumentException>(() => _urls.AddAsync(Url(counties: ["Bradley", "boone"])));
        await _urls.AddAsync(Url(counties: ["Boone"]));
        await _urls.AddAsync(Url(type: "measure_data", counties: ["Boone", "Bradley"]));
        Assert.Equal(3, (await _urls.ListAsync("AR")).Count);
    }

    [Fact]
    public async Task Edits_DeleteAndRevert_AreAllVersions()
    {
        var url = await _urls.AddAsync(Url(counties: ["Washington"]));
        await _urls.UpdateAsync(url.UrlId, Url(counties: ["Boone"]));
        await _urls.UpdateAsync(url.UrlId, Url(counties: ["Boone"], city: "Little Rock"));
        Assert.True(await _urls.DeleteAsync(url.UrlId, "tester"));
        Assert.False(await _urls.DeleteAsync(url.UrlId, "tester"));

        Assert.Empty(await _urls.ListAsync("AR"));
        await Assert.ThrowsAsync<ArgumentException>(() => _urls.UpdateAsync(url.UrlId, Url()));

        var history = await _urls.HistoryAsync(url.UrlId);
        Assert.Equal(["deleted", "edited", "edited", "added"], history.Select(v => v.Action));
        await Assert.ThrowsAsync<ArgumentException>(() => _urls.RevertAsync(url.UrlId, history[0].VersionId, "tester"));

        var reverted = await _urls.RevertAsync(url.UrlId, history[^1].VersionId, "reverter");
        Assert.Equal(["Washington"], reverted!.Counties);
        Assert.Null(reverted.City);
        Assert.Equal("reverter", Assert.Single(await _urls.ListAsync("AR")).UpdatedBy);
        Assert.Equal(RegionUrlStore.Reverted, (await _urls.HistoryAsync(url.UrlId))[0].Action);
    }

    [Fact]
    public async Task MissingUrls_AreNull()
    {
        Assert.Null(await _urls.UpdateAsync(99, Url()));
        Assert.Null(await _urls.RevertAsync(99, 1, "tester"));
        Assert.False(await _urls.DeleteAsync(99, "tester"));

        var other = await _urls.AddAsync(Url());
        var mine = await _urls.AddAsync(Url(type: "measure_data"));
        var otherVersion = (await _urls.HistoryAsync(other.UrlId))[0].VersionId;
        await Assert.ThrowsAsync<ArgumentException>(() => _urls.RevertAsync(mine.UrlId, otherVersion, "tester"));
    }

    [Fact]
    public async Task StateHistory_KeepsAUrlMovedToAnotherState()
    {
        var moved = await _urls.AddAsync(Url());
        await _urls.UpdateAsync(moved.UrlId, Url(state: "TX"));
        await _urls.AddAsync(Url(state: "TX", type: "measure_data"));

        Assert.Equal(2, (await _urls.StateHistoryAsync("AR")).Count);
        Assert.Equal(3, (await _urls.StateHistoryAsync("TX")).Count);
        Assert.Empty(await _urls.ListAsync("AR"));
    }

    [Fact]
    public async Task LastUsed_ComesFromTheStatesRetrieves_WhileEachVersionWasCurrent()
    {
        var url = await _urls.AddAsync(Url());
        await _urls.UpdateAsync(url.UrlId, Url(city: "Little Rock"));
        await using var cn = await _db.OpenStagingAsync();
        await cn.ExecuteAsync(
            $"""
            UPDATE RegionUrlVersions SET EditedAt = IF(Action = 'added', '2026-10-02 09:00:00', '2026-10-12 09:00:00');
            INSERT INTO Captures (CaptureId, StateCode, Year, Status, Source, StartedAt) VALUES
                (1, 'AR', 2026, 'succeeded', 'web', '2026-10-08 10:00:00'),
                (2, 'AR', 2026, 'succeeded', 'web', '2026-10-15 10:00:00'),
                (3, 'TX', 2026, 'succeeded', 'web', '2026-10-20 10:00:00');
            INSERT INTO CaptureFetches (CaptureId, Seq, FetchedAt, Method, Url, DurationMs) VALUES
                (1, 1, '2026-10-08 10:00:01', 'GET', '{Candidates}', 5),
                (2, 1, '2026-10-15 10:00:01', 'GET', '{Candidates}', 5),
                (3, 1, '2026-10-20 10:00:01', 'GET', '{Candidates}', 5);
            """);

        Assert.Equal("2026-10-15T10:00:00Z", Assert.Single(await _urls.ListAsync("AR")).LastUsedAt);
        Assert.Equal(["2026-10-15T10:00:00Z", "2026-10-08T10:00:00Z"], (await _urls.HistoryAsync(url.UrlId)).Select(v => v.LastUsedAt));
    }

    [Fact]
    public async Task Counties_ComeFromRunsAndUrls()
    {
        await using var cn = await _db.OpenStagingAsync();
        await cn.ExecuteAsync(
            """
            INSERT INTO PassCountyDirectory (PassId, StateCode, CountyName) VALUES (1, 'AR', 'Boone'), (1, 'AR', 'Ashley'), (2, 'TX', 'Harris');
            INSERT INTO RunCountyBallots (RunId, StateCode, County) VALUES (1, 'AR', 'Baxter'), (1, 'AR', 'boone');
            """);
        await _urls.AddAsync(Url(counties: ["Calhoun"]));

        Assert.Equal(["Ashley", "Baxter", "Boone", "Calhoun"], await _urls.CountiesAsync("AR"));
    }
}
