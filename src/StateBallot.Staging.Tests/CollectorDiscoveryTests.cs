using StateBallot.Core;
using Xunit;

namespace StateBallot.Staging.Tests;

/// <summary>
/// Constructs every registered collector the way CollectorRunner does (factory
/// invoked with the pipeline data root as the DB-first flow passes it, no
/// --output-root). Catches constructor-contract regressions like a collector
/// still calling DataPaths.FromStateOutputDir on a path that isn't a real
/// data/output/&lt;xx&gt; dir - no network or database needed, since this never
/// calls CollectAsync.
/// </summary>
public class CollectorDiscoveryTests
{
    [Fact]
    public void ImplementedCatalogCodes_AreAllDiscoverable()
    {
        var dataRoot = CollectorRunner.FindDataRoot();
        var catalog = StateCatalog.LoadFromDataRoot(dataRoot);
        var collectors = CollectorDiscovery.Discover();

        foreach (var code in catalog.ImplementedCodes)
            Assert.True(collectors.ContainsKey(code),
                $"'{code}' is marked implemented in state_catalog.json but no [StateCode(\"{code}\")] collector was discovered.");
    }

    [Fact]
    public void EveryDiscoveredCollector_ConstructsWithoutThrowing()
    {
        var dataRoot = CollectorRunner.FindDataRoot();
        using var fetcher = new HttpFetcher();
        var collectors = CollectorDiscovery.Discover();

        Assert.NotEmpty(collectors);

        foreach (var (code, factory) in collectors)
        {
            var ex = Record.Exception(() => factory(fetcher, DateTime.UtcNow.Year, dataRoot, dataRoot));
            Assert.True(ex is null, $"{code} collector constructor threw: {ex}");
        }
    }
}
