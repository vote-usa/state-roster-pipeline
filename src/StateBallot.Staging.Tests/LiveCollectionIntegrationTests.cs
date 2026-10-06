using StateBallot.Core;
using Testcontainers.MySql;
using Xunit;

namespace StateBallot.Staging.Tests;

/// <summary>
/// Runs a real collect-and-persist cycle - live network sources, a throwaway
/// Testcontainers MySQL, no mocking - for every state the catalog marks
/// implemented. This is the gap the original PR review flagged: nothing else
/// exercises RunWriter/CollectorRunner against a real database, and every DB
/// write bug found by hand this session (WA county truncation, MD FilingDate,
/// NM MeasureId) would have shown up here immediately.
///
/// Slow and network-dependent by design, so it's tagged out of the default
/// `dotnet test` run. Run explicitly with:
///   dotnet test --filter Category=Integration
/// </summary>
[Trait("Category", "Integration")]
public sealed class LiveCollectionIntegrationTests : IAsyncLifetime
{
    private readonly MySqlContainer _mysql = new MySqlBuilder("mysql:8.4")
        .WithDatabase("roster_staging")
        .WithUsername("roster")
        .WithPassword("roster")
        .Build();

    private StagingDb _db = null!;

    public async Task InitializeAsync()
    {
        await _mysql.StartAsync();
        _db = new StagingDb(_mysql.GetConnectionString(), _mysql.GetConnectionString());
        await new Migrator(_db.StagingConnectionString).ApplyAsync();
    }

    public Task DisposeAsync() => _mysql.DisposeAsync().AsTask();

    public static IEnumerable<object[]> ImplementedStateCodes()
    {
        var catalog = StateCatalog.LoadFromDataRoot(CollectorRunner.FindDataRoot());
        return catalog.ImplementedCodes.Order().Select(code => new object[] { code });
    }

    [Theory]
    [MemberData(nameof(ImplementedStateCodes))]
    public async Task Collect_PersistsWithNoUnassignedRows(string stateCode)
    {
        var runner = new CollectorRunner(_db);
        var request = new RunRequest(stateCode, DateTime.UtcNow.Year) { Source = "test" };

        var outcome = await runner.RunAsync(request, TextWriter.Null);

        Assert.NotEmpty(outcome.Runs);
        Assert.Equal(0, outcome.UnassignedRowCount);
    }
}
