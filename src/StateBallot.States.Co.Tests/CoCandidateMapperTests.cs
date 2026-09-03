using StateBallot.Core;

namespace StateBallot.States.Co.Tests;

public class CoCandidateMapperTests
{
    private static Election PrimaryElection() =>
        CoCandidateMapper.ToElection("Primary", new DateOnly(2026, 6, 30), "https://example.com/calendar.pdf");

    private static Election GeneralElection() =>
        CoCandidateMapper.ToElection("General", new DateOnly(2026, 11, 3), "https://example.com/calendar.pdf");

    [Fact]
    public void ToElection_BuildsIdNameAndType()
    {
        var election = PrimaryElection();

        Assert.Equal("CO", election.State);
        Assert.Equal("2026-primary", election.ElectionId);
        Assert.Equal("2026 Primary Election", election.Name);
        Assert.Equal(new DateOnly(2026, 6, 30), election.ElectionDate);
        Assert.Equal("Primary", election.ElectionType);
        Assert.Equal("state", election.Jurisdiction);
    }

    private static Dictionary<string, string> Row(params (string Key, string Value)[] fields)
    {
        var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in fields)
            row[key] = value;
        return row;
    }

    // Mirrors data/input/co/candidate_field_map.json.
    private static Dictionary<string, string> FieldMap() => new() { ["Party"] = "Party" };

    [Fact]
    public void ToCandidateRow_StatewideOffice_UpperCaseStateMarker_NoDistrict()
    {
        var row = Row(("Candidate Name", "Mark Baisley"), ("Office", "US Senate"), ("District", "State"), ("Party", "Republican Party"));

        var candidate = CoCandidateMapper.ToCandidateRow(row, GeneralElection(), "https://example.com/general.xlsx", FieldMap());

        Assert.Equal("US Senate", candidate.Office);
        Assert.Null(candidate.District);
        Assert.Null(candidate.County);
        Assert.Equal("Mark Baisley", candidate.CandidateName);
        Assert.Equal("Republican Party", candidate.Party);
    }

    [Fact]
    public void ToCandidateRow_StatewideOffice_StatewideMarker_NoDistrict()
    {
        var row = Row(("Candidate Name", "Julie Gonzales"), ("Office", "US Senate"), ("District", "Statewide"), ("Party", "Democratic Party"));

        var candidate = CoCandidateMapper.ToCandidateRow(row, PrimaryElection(), "https://example.com/primary.xlsx", FieldMap());

        Assert.Null(candidate.District);
        Assert.Null(candidate.County);
    }

    [Fact]
    public void ToCandidateRow_DistrictedOffice_NumericDistrict()
    {
        var row = Row(("Candidate Name", "Diana DeGette"), ("Office", "US House of Representatives"), ("District", "1"), ("Party", "Democratic Party"));

        var candidate = CoCandidateMapper.ToCandidateRow(row, GeneralElection(), "https://example.com/general.xlsx", FieldMap());

        Assert.Equal("1", candidate.District);
        Assert.Null(candidate.County);
    }

    [Fact]
    public void ToCandidateRow_CountyCourt_DistrictValueBecomesCounty()
    {
        var row = Row(("Candidate Name", "Dana Nichols"), ("Office", "County Court"), ("District", "Weld"));

        var candidate = CoCandidateMapper.ToCandidateRow(row, GeneralElection(), "https://example.com/general.xlsx", FieldMap());

        Assert.Null(candidate.District);
        Assert.Equal("Weld", candidate.County);
    }

    [Fact]
    public void ToCandidateRow_AssociateCountyCourt_DistrictValueBecomesCounty()
    {
        var row = Row(("Candidate Name", "Jane Doe"), ("Office", "Associate County Court"), ("District", "Rio Blanco"));

        var candidate = CoCandidateMapper.ToCandidateRow(row, GeneralElection(), "https://example.com/general.xlsx", FieldMap());

        Assert.Null(candidate.District);
        Assert.Equal("Rio Blanco", candidate.County);
    }

    [Fact]
    public void ToCandidateRow_RtdSubdistrictLetter_KeptAsDistrictNotCounty()
    {
        var row = Row(("Candidate Name", "Jane Doe"), ("Office", "RTD Board of Directors"), ("District", "B"));

        var candidate = CoCandidateMapper.ToCandidateRow(row, GeneralElection(), "https://example.com/general.xlsx", FieldMap());

        Assert.Equal("B", candidate.District);
        Assert.Null(candidate.County);
    }

    [Fact]
    public void ToCandidateRow_WriteInY_SetsWriteInStatus()
    {
        var row = Row(("Candidate Name", "Jane Doe"), ("Office", "Governor"), ("District", "State"), ("Write In?", "Y"));

        var candidate = CoCandidateMapper.ToCandidateRow(row, GeneralElection(), "https://example.com/general.xlsx", FieldMap());

        Assert.Equal("Write-in", candidate.Status);
    }

    [Fact]
    public void ToCandidateRow_WriteInN_StatusIsNull()
    {
        var row = Row(("Candidate Name", "Jane Doe"), ("Office", "Governor"), ("District", "State"), ("Write In?", "N"));

        var candidate = CoCandidateMapper.ToCandidateRow(row, GeneralElection(), "https://example.com/general.xlsx", FieldMap());

        Assert.Null(candidate.Status);
    }

    [Fact]
    public void ToCandidateRow_BlankParty_IsNullNotEmptyString()
    {
        var row = Row(("Candidate Name", "Jane Doe"), ("Office", "District Court"), ("District", "1"), ("Party", ""));

        var candidate = CoCandidateMapper.ToCandidateRow(row, GeneralElection(), "https://example.com/general.xlsx", FieldMap());

        Assert.Null(candidate.Party);
    }

    [Fact]
    public void ToCandidateRow_MissingColumns_LeaveFieldsNullNotThrowing()
    {
        var row = Row(("Office", "Governor"));

        var candidate = CoCandidateMapper.ToCandidateRow(row, GeneralElection(), "https://example.com/general.xlsx", FieldMap());

        Assert.Equal("", candidate.CandidateName);
        Assert.Null(candidate.Party);
        Assert.Null(candidate.District);
        Assert.Null(candidate.County);
    }
}
