using StateBallot.Core;

namespace StateBallot.States.Nm.Tests;

public class NmCandidateMapperTests
{
    private static Election GeneralElection() =>
        NmCandidateMapper.ToElection("General", new DateOnly(2026, 11, 3), "https://example.com/elections");

    [Fact]
    public void ToElection_BuildsIdNameAndType()
    {
        var election = GeneralElection();

        Assert.Equal("NM", election.State);
        Assert.Equal("2026-general", election.ElectionId);
        Assert.Equal("2026 General Election", election.Name);
        Assert.Equal(new DateOnly(2026, 11, 3), election.ElectionDate);
        Assert.Equal("General", election.ElectionType);
        Assert.Equal("state", election.Jurisdiction);
    }

    private static Dictionary<string, string> Row(params (string Key, string Value)[] fields)
    {
        var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in fields)
            row[key] = value;
        return row;
    }

    // Mirrors data/input/nm/candidate_field_map.json.
    private static Dictionary<string, string> FieldMap() => new()
    {
        ["Party"] = "Party",
        ["FilingDate"] = "Filing Date/Time",
        ["Phone"] = "Phone",
        ["Email"] = "Email",
        ["Website"] = "Website",
        ["MailingAddressLine"] = "Address",
        ["MailingCity"] = "City",
        ["MailingState"] = "State",
        ["MailingZip"] = "Zip",
        ["Status"] = "Status",
    };

    [Fact]
    public void ToCandidateRow_StatewideOffice_NoDistrictOrCounty()
    {
        var row = Row(
            ("Contest", "United States Senator"), ("District", ""), ("Filing County", ""),
            ("First Name", "BEN"), ("Middle Name", "R"), ("Last Name", "LUJAN"), ("Party", "DEM"));

        var candidate = NmCandidateMapper.ToCandidateRow(row, GeneralElection(), "https://example.com/export", FieldMap());

        Assert.Equal("United States Senator", candidate.Office);
        Assert.Null(candidate.District);
        Assert.Null(candidate.County);
        Assert.Equal("BEN R LUJAN", candidate.CandidateName);
        Assert.Equal("DEM", candidate.Party);
    }

    [Fact]
    public void ToCandidateRow_JointTicket_JoinsAllThreeNameColumns()
    {
        var row = Row(
            ("Contest", "Governor and Lieutenant Governor"), ("District", ""), ("Filing County", ""),
            ("First Name", "GREGGORY D HULL"), ("Middle Name", "AND"), ("Last Name", "DAVID M GALLEGOS"), ("Party", "REP"));

        var candidate = NmCandidateMapper.ToCandidateRow(row, GeneralElection(), "https://example.com/export", FieldMap());

        Assert.Equal("GREGGORY D HULL AND DAVID M GALLEGOS", candidate.CandidateName);
    }

    [Fact]
    public void ToCandidateRow_UsHouseDistrict_NormalizedToBareNumber()
    {
        var row = Row(("Contest", "United States Representative"), ("District", "DISTRICT 2"), ("Filing County", ""),
            ("First Name", "Jane"), ("Last Name", "Doe"));

        var candidate = NmCandidateMapper.ToCandidateRow(row, GeneralElection(), "https://example.com/export", FieldMap());

        Assert.Equal("2", candidate.District);
        Assert.Null(candidate.County);
    }

    [Fact]
    public void ToCandidateRow_CountyCommissionerSubDistrict_NormalizedNumber_KeepsCounty()
    {
        var row = Row(("Contest", "County Commissioner by Commissioner District"), ("District", "COUNTY COMMISSION DISTRICT 1"),
            ("Filing County", "Chaves"), ("First Name", "Jane"), ("Last Name", "Doe"));

        var candidate = NmCandidateMapper.ToCandidateRow(row, GeneralElection(), "https://example.com/export", FieldMap());

        Assert.Equal("1", candidate.District);
        Assert.Equal("Chaves", candidate.County);
    }

    [Fact]
    public void ToCandidateRow_CountySheriff_BlankDistrict_CountyFromFilingCounty()
    {
        var row = Row(("Contest", "County Sheriff"), ("District", ""), ("Filing County", "Bernalillo"),
            ("First Name", "Jane"), ("Last Name", "Doe"));

        var candidate = NmCandidateMapper.ToCandidateRow(row, GeneralElection(), "https://example.com/export", FieldMap());

        Assert.Null(candidate.District);
        Assert.Equal("Bernalillo", candidate.County);
    }

    [Fact]
    public void ToCandidateRow_DistrictCourtJudge_JudicialDistrictKeptVerbatim_NoCounty()
    {
        // A judicial district spans multiple counties - Filing County is just
        // where this one candidate personally filed, not the race's true scope.
        var row = Row(("Contest", "District Court Judge"), ("District", "10TH JUDICIAL DISTRICT"),
            ("Filing County", "Quay"), ("First Name", "Jane"), ("Last Name", "Doe"));

        var candidate = NmCandidateMapper.ToCandidateRow(row, GeneralElection(), "https://example.com/export", FieldMap());

        Assert.Equal("10TH JUDICIAL DISTRICT", candidate.District);
        Assert.Null(candidate.County);
    }

    [Fact]
    public void ToCandidateRow_MagistrateJudge_DivisionKeptVerbatim_CountyFromFilingCounty()
    {
        var row = Row(("Contest", "Magistrate Judge"), ("District", "DIVISION 1"),
            ("Filing County", "Cibola"), ("First Name", "Jane"), ("Last Name", "Doe"));

        var candidate = NmCandidateMapper.ToCandidateRow(row, GeneralElection(), "https://example.com/export", FieldMap());

        Assert.Equal("DIVISION 1", candidate.District);
        Assert.Equal("Cibola", candidate.County);
    }

    [Fact]
    public void ToCandidateRow_MunicipalJudge_KeptVerbatim_NoCounty()
    {
        var row = Row(("Contest", "Municipal Judge"), ("District", "MUNICIPAL DISTRICT 32"),
            ("Filing County", "Los Alamos"), ("First Name", "Jane"), ("Last Name", "Doe"));

        var candidate = NmCandidateMapper.ToCandidateRow(row, GeneralElection(), "https://example.com/export", FieldMap());

        Assert.Equal("MUNICIPAL DISTRICT 32", candidate.District);
        Assert.Null(candidate.County);
    }

    [Fact]
    public void ToCandidateRow_MapsContactAndStatusFields()
    {
        var row = Row(
            ("Contest", "Governor"), ("District", ""), ("Filing County", ""),
            ("First Name", "Jane"), ("Last Name", "Doe"),
            ("Filing Date/Time", "2/3/2026 9:44:07 AM"), ("Phone", "(505) 225-8640"),
            ("Email", "jane@example.com"), ("Website", "janefornm.com"),
            ("Address", "5 ENTRADA CELEDON Y NESTORA"), ("City", "SANTA FE"), ("State", "NM"), ("Zip", "87506"),
            ("Status", "Qualified"));

        var candidate = NmCandidateMapper.ToCandidateRow(row, GeneralElection(), "https://example.com/export", FieldMap());

        Assert.Equal("2/3/2026 9:44:07 AM", candidate.FilingDate);
        Assert.Equal("(505) 225-8640", candidate.Phone);
        Assert.Null(candidate.CampaignPhone);
        Assert.Equal("jane@example.com", candidate.Email);
        Assert.Equal("janefornm.com", candidate.Website);
        Assert.Equal("5 ENTRADA CELEDON Y NESTORA", candidate.MailingAddressLine);
        Assert.Equal("SANTA FE", candidate.MailingCity);
        Assert.Equal("NM", candidate.MailingState);
        Assert.Equal("87506", candidate.MailingZip);
        Assert.Equal("Qualified", candidate.Status);
    }

    [Fact]
    public void IsJudicialRetention_BarePrefix_True()
    {
        var row = Row(("Contest", "Judicial Retention"));
        Assert.True(NmCandidateMapper.IsJudicialRetention(row));
    }

    [Fact]
    public void IsJudicialRetention_CompositeSeat_True()
    {
        var row = Row(("Contest", "Judicial Retention Judge of the Metropolitan Court DIVISION 2"));
        Assert.True(NmCandidateMapper.IsJudicialRetention(row));
    }

    [Fact]
    public void IsJudicialRetention_OrdinaryContest_False()
    {
        var row = Row(("Contest", "Governor"));
        Assert.False(NmCandidateMapper.IsJudicialRetention(row));
    }

    [Fact]
    public void ToCandidateRow_MissingColumns_LeaveFieldsNullNotThrowing()
    {
        var row = Row(("Contest", "Governor"));

        var candidate = NmCandidateMapper.ToCandidateRow(row, GeneralElection(), "https://example.com/export", FieldMap());

        Assert.Equal("", candidate.CandidateName);
        Assert.Null(candidate.Party);
        Assert.Null(candidate.Email);
        Assert.Null(candidate.District);
        Assert.Null(candidate.County);
    }
}
