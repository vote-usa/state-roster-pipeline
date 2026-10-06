namespace StateBallot.States.Ca.Tests;

public class UpcomingElectionsScraperTests
{
    [Fact]
    public void ToElection_Statewide_UsesStateJurisdictionAndUrlSlugAsId()
    {
        var election = UpcomingElectionsScraper.ToElection(
            linkText: "General Election - November 3, 2026",
            name: "General Election",
            date: new DateOnly(2026, 11, 3),
            url: "https://www.sos.ca.gov/elections/upcoming-elections/2026-general",
            isStatewide: true,
            fallbackSourceUrl: "https://www.sos.ca.gov/elections/upcoming-elections");

        Assert.Equal("2026-general", election.ElectionId);
        Assert.Equal("General Election - November 3, 2026", election.Name);
        Assert.Equal(new DateOnly(2026, 11, 3), election.ElectionDate);
        Assert.Equal("General", election.ElectionType);
        Assert.Equal("state", election.Jurisdiction);
        Assert.Equal("https://www.sos.ca.gov/elections/upcoming-elections/2026-general", election.SourceUrl);
    }

    [Fact]
    public void ToElection_SpecialVacancy_DerivesJurisdictionFromNameAndElectionType()
    {
        var election = UpcomingElectionsScraper.ToElection(
            linkText: "Congressional District 14, Special General Election - August 18, 2026",
            name: "Congressional District 14, Special General Election",
            date: new DateOnly(2026, 8, 18),
            url: "https://www.sos.ca.gov/elections/upcoming-elections/2026-cd14",
            isStatewide: false,
            fallbackSourceUrl: "https://www.sos.ca.gov/elections/upcoming-elections");

        Assert.Equal("Congressional District 14", election.Jurisdiction);
        Assert.Equal("Special General", election.ElectionType);
    }

    [Fact]
    public void ToElection_NoUrl_FallsBackToNameAsIdAndFallbackSourceUrl()
    {
        var election = UpcomingElectionsScraper.ToElection(
            linkText: "Special Election - August 25, 2026",
            name: "Special Election",
            date: new DateOnly(2026, 8, 25),
            url: null,
            isStatewide: false,
            fallbackSourceUrl: "https://www.sos.ca.gov/elections/upcoming-elections");

        Assert.Equal("Special Election", election.ElectionId);
        Assert.Equal("https://www.sos.ca.gov/elections/upcoming-elections", election.SourceUrl);
        Assert.Equal("Special", election.ElectionType);
    }
}
