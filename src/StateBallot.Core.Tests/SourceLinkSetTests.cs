namespace StateBallot.Core.Tests;

public class SourceLinkSetTests
{
    private static SourceLinkSet Set(params SourceLink[] links) => new("MD", links, [], "test");

    private static SourceLink Link(string key, string url, string? variant = null) =>
        new() { Key = key, Url = url, Variant = variant };

    [Fact]
    public void Url_FillsPlaceholders()
    {
        var set = Set(Link("voter-guide", "https://x.test/guide?e={electionId}&c={countyCode}&y={year}"));

        Assert.Equal("https://x.test/guide?e=91&c=&y=2026", set.Url("voter-guide", year: 2026, electionId: "91", countyCode: ""));
    }

    [Fact]
    public void Url_IgnoresValuesTheTemplateDoesNotUse()
    {
        var set = Set(Link("home", "https://x.test"));

        Assert.Equal("https://x.test", set.Url("home", year: 2026));
    }

    [Fact]
    public void Url_WhenAPlaceholderIsNotSupplied_Throws()
    {
        var set = Set(Link("elections-page", "https://x.test/{year}/index.html"));

        var ex = Assert.Throws<InvalidOperationException>(() => set.Url("elections-page"));
        Assert.Contains("{year}", ex.Message);
    }

    [Fact]
    public void Url_MatchesVariantsIgnoringCase()
    {
        var set = Set(
            Link("candidate-list", "https://x.test/primary.csv", "primary"),
            Link("candidate-list", "https://x.test/general.csv", "General"));

        Assert.Equal("https://x.test/primary.csv", set.Url("candidate-list", "Primary"));
        Assert.Equal("https://x.test/general.csv", set.Url("candidate-list", "general"));
    }

    [Fact]
    public void Url_ForAMissingOrInactiveLink_Throws()
    {
        var set = Set(new SourceLink { Key = "retired", Url = "https://x.test", Active = false });

        Assert.Throws<InvalidOperationException>(() => set.Url("retired"));
        Assert.Throws<InvalidOperationException>(() => set.Url("never-configured"));
    }

    [Fact]
    public void Constructor_RejectsAnUnknownPlaceholder()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Set(Link("page", "https://x.test/{yaer}")));
        Assert.Contains("{yaer}", ex.Message);
    }

    [Fact]
    public void Constructor_RejectsADuplicateKeyAndVariant()
    {
        Assert.Throws<InvalidOperationException>(() => Set(
            Link("candidate-list", "https://x.test/a", "primary"),
            Link("candidate-list", "https://x.test/b", "PRIMARY")));
    }

    [Fact]
    public void Parameter_ReturnsTheValueOrThrows()
    {
        var set = new SourceLinkSet("SD", [], [new SourceParameter { Key = "election-id", Variant = "General", Value = "774" }], "test");

        Assert.Equal("774", set.Parameter("election-id", "general"));
        Assert.Null(set.FindParameter("election-id", "primary"));
        Assert.Throws<InvalidOperationException>(() => set.Parameter("election-id", "primary"));
    }

    [Fact]
    public void VerificationSources_ListsActiveVerificationLinksForTheYear()
    {
        var config = new TestConfig
        {
            Links = Set(
                Link("home", "https://x.test"),
                new SourceLink { Key = "cross-check", Url = "https://b.test/MD_{year}", Format = "html", Kind = SourceLinkKind.Verification }),
        };

        var entry = Assert.Single(config.VerificationSources(2026));
        Assert.Equal("https://b.test/MD_2026", entry.Url);
        Assert.Equal("html", entry.Format);
    }

    [Fact]
    public void ToJson_OmitsDefaultsAndRoundTrips()
    {
        var set = Set(
            Link("candidate-list", "https://x.test/{year}?a=1&b=2", "primary"),
            new SourceLink { Key = "home", Url = "https://x.test", Kind = SourceLinkKind.Home, Notes = "Check <this> by hand." });

        var json = set.ToJson();
        var path = Path.Combine(Path.GetTempPath(), $"links-{Guid.NewGuid():N}.json");
        try
        {
            File.WriteAllText(path, json);
            Assert.Equal(json, SourceLinkSet.LoadFile(path, "MD").ToJson());
        }
        finally
        {
            File.Delete(path);
        }

        Assert.DoesNotContain("\"active\"", json);
        Assert.DoesNotContain("\"fetch\"", json);
        Assert.Contains("?a=1&b=2", json);
        Assert.Contains("<this>", json);
    }

    private sealed class TestConfig : SourceConfigBase;
}
