namespace StateBallot.Core.Tests;

public class OcdDivisionIdTests
{
    [Theory]
    [InlineData("WA", "U.S. Representative", "Congressional District 1", "ocd-division/country:us/state:wa/cd:1")]
    [InlineData("WA", "State Senator", "Legislative District 6", "ocd-division/country:us/state:wa/sldu:6")]
    [InlineData("WA", "State Representative Pos. 1", "Legislative District 1", "ocd-division/country:us/state:wa/sldl:1")]
    [InlineData("WA", "U.S. Representative", "Congressional District No. 5", "ocd-division/country:us/state:wa/cd:5")]
    [InlineData("WA", "State Representative Pos. 2", "Legislative District No. 9", "ocd-division/country:us/state:wa/sldl:9")]
    [InlineData("NC", "US HOUSE OF REPRESENTATIVES", "09", "ocd-division/country:us/state:nc/cd:9")]
    [InlineData("NC", "NC HOUSE OF REPRESENTATIVES", "012", "ocd-division/country:us/state:nc/sldl:12")]
    [InlineData("NC", "NC STATE SENATE", "01", "ocd-division/country:us/state:nc/sldu:1")]
    [InlineData("SC", "U.S. House of Representatives", "5", "ocd-division/country:us/state:sc/cd:5")]
    [InlineData("SC", "State House of Representatives", "100", "ocd-division/country:us/state:sc/sldl:100")]
    [InlineData("MS", "United States House of Representatives", "1", "ocd-division/country:us/state:ms/cd:1")]
    [InlineData("CO", "US House of Representatives", "3", "ocd-division/country:us/state:co/cd:3")]
    [InlineData("CO", "State House of Representatives", "5", "ocd-division/country:us/state:co/sldl:5")]
    [InlineData("VA", "Member, House of Representatives", "5", "ocd-division/country:us/state:va/cd:5")]
    public void ForCandidate_LegislativeSeatWithPlainDistrict_MapsToDistrict(
        string state, string office, string district, string expected)
    {
        Assert.Equal(expected, OcdDivisionId.ForCandidate(state, office, district, null));
    }

    [Theory]
    [InlineData("VT", "State Representative", "ADD 1")]
    [InlineData("VT", "State Senator", "BEN RUT")]
    [InlineData("MS", "House of Representatives", "12")]
    [InlineData("WA", "Commissioner District 4", "Port Of Bellingham Commissioner District 4")]
    public void ForCandidate_DistrictThatCannotBeMapped_IsNull(string state, string office, string district)
    {
        Assert.Null(OcdDivisionId.ForCandidate(state, office, district, null));
    }

    [Fact]
    public void ForCandidate_AtLargeUsHouseWithNoDistrict_IsState()
    {
        Assert.Equal("ocd-division/country:us/state:vt", OcdDivisionId.ForCandidate("VT", "U.S. Representative", null, null));
    }
}
