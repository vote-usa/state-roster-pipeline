namespace StateBallot.Staging.Tests;

public class RegionUrlRulesTests
{
    private static RegionUrlStore.SaveUrl Request(
        string state = "ar", string[]? counties = null, string? city = null,
        string url = "https://www.sos.arkansas.gov/elections", string type = "candidate_data", string by = "tester") =>
        new(state, counties ?? [], city, url, type, by);

    [Fact]
    public void Clean_TrimsAndNormalizes()
    {
        var clean = RegionUrlStore.Clean(Request(
            state: " ar ", counties: [" Boone ", "", "Bradley", "boone"], city: "  ", url: " https://sos.ar.gov/x ", by: " Kim "));

        Assert.Equal("AR", clean.StateCode);
        Assert.Equal(["Boone", "Bradley"], clean.Counties);
        Assert.Null(clean.City);
        Assert.Equal("https://sos.ar.gov/x", clean.Url);
        Assert.Equal("Kim", clean.EditedBy);
    }

    [Theory]
    [InlineData("", "Enter a URL.")]
    [InlineData("sos.ar.gov/elections", "starting with http")]
    [InlineData("ftp://sos.ar.gov/list.csv", "starting with http")]
    [InlineData("javascript:alert(1)", "starting with http")]
    public void Clean_RefusesAnythingButAnHttpUrl(string url, string message) =>
        Assert.Contains(message, Assert.Throws<ArgumentException>(() => RegionUrlStore.Clean(Request(url: url))).Message);

    [Fact]
    public void Clean_RefusesAnUnknownType() =>
        Assert.Throws<ArgumentException>(() => RegionUrlStore.Clean(Request(type: "roster")));

    [Fact]
    public void Clean_NeedsAName() =>
        Assert.Contains("your name", Assert.Throws<ArgumentException>(() => RegionUrlStore.Clean(Request(by: " "))).Message);

    [Fact]
    public void Clean_RefusesALongCityOrCounty()
    {
        Assert.Throws<ArgumentException>(() => RegionUrlStore.Clean(Request(city: new string('x', 101))));
        Assert.Throws<ArgumentException>(() => RegionUrlStore.Clean(Request(counties: [new string('x', 101)])));
    }

    [Fact]
    public void LastUsed_IsTheLatestUseOfThatUrlInThatState_InsideTheWindow()
    {
        const string url = "https://sos.ar.gov/list.csv";
        RegionUrlStore.UrlUse[] uses =
        [
            new("AR", url, new DateTime(2026, 10, 2)),
            new("AR", url, new DateTime(2026, 10, 8)),
            new("AR", url, new DateTime(2026, 10, 15)),
            new("AR", "https://sos.ar.gov/other", new DateTime(2026, 10, 20)),
            new("TX", url, new DateTime(2026, 10, 21)),
        ];

        Assert.Equal(new DateTime(2026, 10, 15), RegionUrlStore.LastUsed(uses, "AR", url));
        Assert.Equal(new DateTime(2026, 10, 8), RegionUrlStore.LastUsed(uses, "AR", url, new DateTime(2026, 10, 8), new DateTime(2026, 10, 12)));
        Assert.Null(RegionUrlStore.LastUsed(uses, "AR", url, new DateTime(2026, 10, 16)));
    }
}
