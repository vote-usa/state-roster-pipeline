using StateBallot.Core;

namespace StateBallot.States.Nc.Tests;

public class NcCandidateMapperTests
{
    private static Election GeneralElection() =>
        NcCandidateMapper.ToElection(new DateOnly(2026, 11, 3), isLatest: true, "https://example.com/candidates.csv");

    private static Election PrimaryElection() =>
        NcCandidateMapper.ToElection(new DateOnly(2026, 3, 3), isLatest: false, "https://example.com/candidates.csv");

    [Fact]
    public void ToElection_LatestDate_IsGeneral()
    {
        var election = GeneralElection();

        Assert.Equal("NC", election.State);
        Assert.Equal("2026-general", election.ElectionId);
        Assert.Equal("2026 General Election", election.Name);
        Assert.Equal("General", election.ElectionType);
    }

    [Fact]
    public void ToElection_NonLatestDate_IsPrimary()
    {
        var election = PrimaryElection();

        Assert.Equal("2026-primary", election.ElectionId);
        Assert.Equal("Primary", election.ElectionType);
    }

    private static Dictionary<string, string> Row(params (string Key, string Value)[] fields)
    {
        var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in fields)
            row[key] = value;
        return row;
    }

    [Fact]
    public void ToCandidateRow_NoDistrictSuffix_LeavesOfficeAndDistrictAsIs()
    {
        var row = Row(("contest_name", "US SENATE"), ("name_on_ballot", "Roy Cooper"), ("party_candidate", "DEM"));

        var candidate = NcCandidateMapper.ToCandidateRow(row, GeneralElection(), "https://example.com/candidates.csv", county: null);

        Assert.Equal("US SENATE", candidate.Office);
        Assert.Null(candidate.District);
        Assert.Null(candidate.County);
        Assert.Equal("Roy Cooper", candidate.CandidateName);
        Assert.Equal("DEM", candidate.Party);
    }

    [Fact]
    public void ToCandidateRow_DistrictSuffix_SplitsOfficeAndDistrict()
    {
        var row = Row(("contest_name", "US HOUSE OF REPRESENTATIVES DISTRICT 09"), ("name_on_ballot", "Jane Doe"));

        var candidate = NcCandidateMapper.ToCandidateRow(row, GeneralElection(), "https://example.com/candidates.csv", county: null);

        Assert.Equal("US HOUSE OF REPRESENTATIVES", candidate.Office);
        Assert.Equal("09", candidate.District);
    }

    [Fact]
    public void ToCandidateRow_SeatSuffix_SplitsOfficeAndSeat()
    {
        var row = Row(("contest_name", "NC SUPREME COURT ASSOCIATE JUSTICE SEAT 01"), ("name_on_ballot", "Jane Doe"));

        var candidate = NcCandidateMapper.ToCandidateRow(row, GeneralElection(), "https://example.com/candidates.csv", county: null);

        Assert.Equal("NC SUPREME COURT ASSOCIATE JUSTICE", candidate.Office);
        Assert.Equal("01", candidate.District);
    }

    /// <summary>
    /// Regression test: "DISTRICT" also appears as part of non-numbered office
    /// titles ("Soil and Water Conservation District Supervisor") - this must
    /// NOT be split, since "SUPERVISOR" isn't a district number. Caught via live
    /// verification against real NC data before this test existed.
    /// </summary>
    [Fact]
    public void ToCandidateRow_DistrictAsPartOfOfficeTitle_DoesNotFalselySplit()
    {
        var row = Row(("contest_name", "ALAMANCE SOIL AND WATER CONSERVATION DISTRICT SUPERVISOR"), ("name_on_ballot", "Jane Doe"));

        var candidate = NcCandidateMapper.ToCandidateRow(row, GeneralElection(), "https://example.com/candidates.csv", county: "ALAMANCE");

        Assert.Equal("ALAMANCE SOIL AND WATER CONSERVATION DISTRICT SUPERVISOR", candidate.Office);
        Assert.Null(candidate.District);
    }

    [Fact]
    public void ToCandidateRow_CountyParameter_IsPassedThroughAsIs()
    {
        var row = Row(("contest_name", "ALAMANCE COUNTY SHERIFF"), ("name_on_ballot", "Jane Doe"));

        var candidate = NcCandidateMapper.ToCandidateRow(row, GeneralElection(), "https://example.com/candidates.csv", county: "ALAMANCE");

        Assert.Equal("ALAMANCE", candidate.County);
    }

    [Fact]
    public void ToCandidateRow_MapsAddressEmailAndFilingDateDirectly()
    {
        var row = Row(
            ("contest_name", "US SENATE"),
            ("name_on_ballot", "Jane Doe"),
            ("street_address", "PO BOX 1190"),
            ("city", "RALEIGH"),
            ("state", "NC"),
            ("zip_code", "27602"),
            ("email", "jane@example.com"),
            ("candidacy_dt", "12/03/2025"));

        var candidate = NcCandidateMapper.ToCandidateRow(row, GeneralElection(), "https://example.com/candidates.csv", county: null);

        Assert.Equal("PO BOX 1190", candidate.MailingAddressLine);
        Assert.Equal("RALEIGH", candidate.MailingCity);
        Assert.Equal("NC", candidate.MailingState);
        Assert.Equal("27602", candidate.MailingZip);
        Assert.Equal("jane@example.com", candidate.Email);
        Assert.Equal("12/03/2025", candidate.FilingDate);
    }

    [Theory]
    [InlineData("2065551234", "", "", "2065551234")]
    [InlineData("", "9195551234", "", "9195551234")]
    [InlineData("", "", "9195559999", "9195559999")]
    [InlineData("", "", "", null)]
    public void ToCandidateRow_Phone_PrefersPhoneThenBusinessThenOfficePhone(
        string phone, string businessPhone, string officePhone, string? expected)
    {
        var row = Row(
            ("contest_name", "US SENATE"),
            ("name_on_ballot", "Jane Doe"),
            ("phone", phone),
            ("business_phone", businessPhone),
            ("office_phone", officePhone));

        var candidate = NcCandidateMapper.ToCandidateRow(row, GeneralElection(), "https://example.com/candidates.csv", county: null);

        Assert.Equal(expected, candidate.Phone);
    }

    [Fact]
    public void ToCandidateRow_MissingColumns_LeaveFieldsNullNotThrowing()
    {
        var row = Row(("contest_name", "US SENATE"));

        var candidate = NcCandidateMapper.ToCandidateRow(row, GeneralElection(), "https://example.com/candidates.csv", county: null);

        Assert.Equal("", candidate.CandidateName);
        Assert.Null(candidate.Party);
        Assert.Null(candidate.Email);
        Assert.Null(candidate.Phone);
    }
}
