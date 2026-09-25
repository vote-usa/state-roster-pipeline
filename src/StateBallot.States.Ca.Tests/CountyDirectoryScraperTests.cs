namespace StateBallot.States.Ca.Tests;

public class CountyDirectoryScraperTests
{
    [Fact]
    public void ToCountyDirectoryRow_ExtractsAddressUpToPhoneLine()
    {
        var lines = new List<string>
        {
            "Tim Dupuis, Registrar of Voters",
            "1225 Fallon St, Suite G-1",
            "Oakland, CA 94612",
            "(510) 272-6933",
            "Mailing Address: PO Box 1234",
        };

        var row = CountyDirectoryScraper.ToCountyDirectoryRow("Alameda", "06001", "https://acgov.org", lines, CaSelectors.Default);

        Assert.Equal("Alameda", row.CountyName);
        Assert.Equal("06001", row.CountyFips);
        Assert.Equal("https://acgov.org", row.ElectionsOfficeUrl);
        Assert.Equal("1225 Fallon St, Suite G-1, Oakland, CA 94612", row.Address);
        Assert.Equal("(510) 272-6933", row.Phone);
    }

    [Fact]
    public void ToCountyDirectoryRow_NoLines_LeavesAddressAndPhoneNull()
    {
        var row = CountyDirectoryScraper.ToCountyDirectoryRow("Alpine", "06003", null, new List<string>(), CaSelectors.Default);

        Assert.Null(row.Address);
        Assert.Null(row.Phone);
        Assert.Null(row.ElectionsOfficeUrl);
    }
}
