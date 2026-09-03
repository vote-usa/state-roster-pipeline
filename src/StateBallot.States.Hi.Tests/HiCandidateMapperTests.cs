using StateBallot.Core;

namespace StateBallot.States.Hi.Tests;

public class HiCandidateMapperTests
{
    private static Election PrimaryElection() =>
        HiCandidateMapper.ToElection("Primary", new DateOnly(2026, 8, 8), "https://example.com/candidates");

    private static Election GeneralElection() =>
        HiCandidateMapper.ToElection("General", new DateOnly(2026, 11, 3), "https://example.com/candidates");

    [Fact]
    public void ToElection_BuildsIdNameAndType()
    {
        var election = PrimaryElection();

        Assert.Equal("HI", election.State);
        Assert.Equal("2026-primary", election.ElectionId);
        Assert.Equal("2026 Primary Election", election.Name);
        Assert.Equal(new DateOnly(2026, 8, 8), election.ElectionDate);
        Assert.Equal("Primary", election.ElectionType);
    }

    [Theory]
    [InlineData("In General", "General")]
    [InlineData("Issued", "Primary")]
    [InlineData("Filed", "Primary")]
    [InlineData("In Primary", "Primary")]
    [InlineData("Withdrawn", "Primary")]
    [InlineData("Void", "Primary")]
    [InlineData("Elected After Primary", "Primary")]
    public void DetermineElection_OnlyInGeneralGoesToGeneral(string status, string expectedType)
    {
        var election = HiCandidateMapper.DetermineElection(status, PrimaryElection(), GeneralElection());

        Assert.Equal(expectedType, election.ElectionType);
    }

    private static Dictionary<string, string> Row(params (string Key, string Value)[] fields)
    {
        var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in fields)
            row[key] = value;
        return row;
    }

    // Mirrors data/input/hi/candidate_field_map.json.
    private static Dictionary<string, string> FieldMap() => new()
    {
        ["Party"] = "Party",
        ["FilingDate"] = "FilingDate",
        ["Email"] = "Email",
        ["Phone"] = "Phone",
        ["Website"] = "Website",
        ["MailingAddressLine"] = "MailingAddress",
        ["Status"] = "Status",
    };

    [Fact]
    public void ToCandidateRow_NoDistrict_LeavesOfficeAsIs()
    {
        var row = Row(("Contests", "GOVERNOR"), ("BallotName", "AKANA, Kelei"), ("Party", "DEMOCRATIC"));

        var candidate = HiCandidateMapper.ToCandidateRow(row, GeneralElection(), "https://example.com/candidates", FieldMap());

        Assert.Equal("GOVERNOR", candidate.Office);
        Assert.Null(candidate.District);
        Assert.Null(candidate.County);
        Assert.Equal("AKANA, Kelei", candidate.CandidateName);
        Assert.Equal("DEMOCRATIC", candidate.Party);
    }

    [Theory]
    [InlineData("STATE SENATOR, DIST 2", "STATE SENATOR", "2")]
    [InlineData("STATE SENATOR, DIST 18 VACANCY", "STATE SENATOR", "18 VACANCY")]
    [InlineData("U.S. REPRESENTATIVE, DIST II", "U.S. REPRESENTATIVE", "II")]
    [InlineData("HAWAII COUNCILMEMBER, DIST 1", "HAWAII COUNCILMEMBER", "1")]
    public void ToCandidateRow_ContestWithDistrict_Splits(string contest, string expectedOffice, string expectedDistrict)
    {
        var row = Row(("Contests", contest), ("BallotName", "Jane Doe"));

        var candidate = HiCandidateMapper.ToCandidateRow(row, GeneralElection(), "https://example.com/candidates", FieldMap());

        Assert.Equal(expectedOffice, candidate.Office);
        Assert.Equal(expectedDistrict, candidate.District);
    }

    [Fact]
    public void ToCandidateRow_ParentheticalDistrict_LeftUnsplit()
    {
        var row = Row(("Contests", "MAUI COUNCILMEMBER (EAST MAUI)"), ("BallotName", "Jane Doe"));

        var candidate = HiCandidateMapper.ToCandidateRow(row, GeneralElection(), "https://example.com/candidates", FieldMap());

        Assert.Equal("MAUI COUNCILMEMBER (EAST MAUI)", candidate.Office);
        Assert.Null(candidate.District);
    }

    [Fact]
    public void ToCandidateRow_SplitsCommaSeparatedCityStateZip()
    {
        var row = Row(
            ("Contests", "GOVERNOR"),
            ("BallotName", "Jane Doe"),
            ("MailingAddress", "87-266 MAALOA ST."),
            ("CityStateZip", "WAIANAE, HI 96792"));

        var candidate = HiCandidateMapper.ToCandidateRow(row, GeneralElection(), "https://example.com/candidates", FieldMap());

        Assert.Equal("87-266 MAALOA ST.", candidate.MailingAddressLine);
        Assert.Equal("WAIANAE", candidate.MailingCity);
        Assert.Equal("HI", candidate.MailingState);
        Assert.Equal("96792", candidate.MailingZip);
    }

    [Fact]
    public void ToCandidateRow_MapsStatusRawAndFilingDateRaw()
    {
        var row = Row(
            ("Contests", "GOVERNOR"),
            ("BallotName", "Jane Doe"),
            ("Status", "Withdrawn"),
            ("FilingDate", "6/1/2026 12:00:00 AM"));

        var candidate = HiCandidateMapper.ToCandidateRow(row, GeneralElection(), "https://example.com/candidates", FieldMap());

        Assert.Equal("Withdrawn", candidate.Status);
        Assert.Equal("6/1/2026 12:00:00 AM", candidate.FilingDate);
    }

    [Fact]
    public void ToCandidateRow_MissingColumns_LeaveFieldsNullNotThrowing()
    {
        var row = Row(("Contests", "GOVERNOR"));

        var candidate = HiCandidateMapper.ToCandidateRow(row, GeneralElection(), "https://example.com/candidates", FieldMap());

        Assert.Equal("", candidate.CandidateName);
        Assert.Null(candidate.Party);
        Assert.Null(candidate.Email);
        Assert.Null(candidate.Status);
    }
}
