using StateBallot.Core;

namespace StateBallot.Staging.Tests;

/// <summary>
/// Checks the tracked seed files at data/input/&lt;xx&gt;/source_links.json, which --migrate loads
/// into SourceLinks. No database needed.
/// </summary>
public class SourceLinkSeedTests
{
    [Fact]
    public void EveryImplementedState_HasAValidSeedFileWithAHomeLink()
    {
        var dataRoot = CollectorRunner.FindDataRoot();
        var catalog = StateCatalog.LoadFromDataRoot(dataRoot);

        foreach (var code in catalog.ImplementedCodes)
        {
            var links = SourceLinkSet.Load(dataRoot, code);
            Assert.Equal(code, links.StateCode);
            Assert.NotEmpty(links.Url("home", year: 2026));
        }
    }

    [Fact]
    public void SeedFiles_AreInTheFormatExportWrites()
    {
        // --export-links rewrites these files; a hand edit in another format would show up
        // as a spurious diff on the next export.
        foreach (var set in LinkStore.LoadSeeds(CollectorRunner.FindDataRoot(), null))
        {
            var path = DataPaths.SourceLinksPath(CollectorRunner.FindDataRoot(), set.StateCode);
            Assert.Equal(File.ReadAllText(path), set.ToJson());
        }
    }
}
