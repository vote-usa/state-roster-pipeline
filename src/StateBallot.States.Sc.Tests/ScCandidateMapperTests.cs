using StateBallot.Core;

namespace StateBallot.States.Sc.Tests;

public class ScCandidateMapperTests
{
    private static Election GeneralElection() =>
        ScCandidateMapper.ToElection("General", new DateOnly(2026, 11, 3), "22596", "https://example.com/elections");

    [Fact]
    public void ToElection_BuildsIdNameAndType()
    {
        var election = GeneralElection();

        Assert.Equal("SC", election.State);
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

    // Mirrors data/input/sc/candidate_field_map.json.
    private static Dictionary<string, string> FieldMap() => new()
    {
        ["Party"] = "Party",
        ["Status"] = "Candidate Status",
    };

    [Fact]
    public void ToCandidateRow_StatewideOffice_NoDistrictOrCounty()
    {
        var row = Row(
            ("Office", "Governor and Lieutenant Governor"), ("Associated Counties", ""),
            ("Name on Ballot", "Michael Addison"), ("Party", "United Citizens"), ("Candidate Status", "Active"));

        var candidate = ScCandidateMapper.ToCandidateRow(row, GeneralElection(), "https://example.com/search", FieldMap());

        Assert.Equal("Governor and Lieutenant Governor", candidate.Office);
        Assert.Null(candidate.District);
        Assert.Null(candidate.County);
        Assert.Equal("Michael Addison", candidate.CandidateName);
        Assert.Equal("United Citizens", candidate.Party);
        Assert.Equal("Active", candidate.Status);
    }

    [Fact]
    public void ToCandidateRow_UsHouseDistrict_SplitsOfficeAndDistrict()
    {
        var row = Row(("Office", "U.S. House of Representatives, District 5"), ("Associated Counties", ""),
            ("Name on Ballot", "Jane Doe"));

        var candidate = ScCandidateMapper.ToCandidateRow(row, GeneralElection(), "https://example.com/search", FieldMap());

        Assert.Equal("U.S. House of Representatives", candidate.Office);
        Assert.Equal("5", candidate.District);
    }

    [Fact]
    public void ToCandidateRow_StateHouseDistrict_SplitsOfficeAndDistrict()
    {
        var row = Row(("Office", "State House of Representatives, District 100"), ("Associated Counties", ""),
            ("Name on Ballot", "Jane Doe"));

        var candidate = ScCandidateMapper.ToCandidateRow(row, GeneralElection(), "https://example.com/search", FieldMap());

        Assert.Equal("State House of Representatives", candidate.Office);
        Assert.Equal("100", candidate.District);
    }

    [Fact]
    public void ToCandidateRow_NoCommaBeforeDistrict_KeptVerbatimNoSplit()
    {
        // SC's own county-by-county naming isn't consistent - "District N" with
        // no comma is a different real shape this collector deliberately doesn't parse further.
        var row = Row(("Office", "County Council District 3"), ("Associated Counties", "CHESTER"),
            ("Name on Ballot", "Jane Doe"));

        var candidate = ScCandidateMapper.ToCandidateRow(row, GeneralElection(), "https://example.com/search", FieldMap());

        Assert.Equal("County Council District 3", candidate.Office);
        Assert.Null(candidate.District);
        Assert.Equal("CHESTER", candidate.County);
    }

    [Fact]
    public void ToCandidateRow_SingleCounty_PassesThrough()
    {
        var row = Row(("Office", "County Council Chair"), ("Associated Counties", "HORRY"), ("Name on Ballot", "Jane Doe"));

        var candidate = ScCandidateMapper.ToCandidateRow(row, GeneralElection(), "https://example.com/search", FieldMap());

        Assert.Equal("HORRY", candidate.County);
    }

    [Fact]
    public void ToCandidateRow_MultiCounty_JoinsWithSemicolon()
    {
        var row = Row(("Office", "Solicitor Circuit 12"), ("Associated Counties", "AIKEN, EDGEFIELD, SALUDA"),
            ("Name on Ballot", "Jane Doe"));

        var candidate = ScCandidateMapper.ToCandidateRow(row, GeneralElection(), "https://example.com/search", FieldMap());

        Assert.Equal("Solicitor Circuit 12", candidate.Office);
        Assert.Null(candidate.District);
        Assert.Equal("AIKEN; EDGEFIELD; SALUDA", candidate.County);
    }

    [Fact]
    public void ToCandidateRow_NoContactFieldsPublished_AllNull()
    {
        var row = Row(("Office", "Governor"), ("Associated Counties", ""), ("Name on Ballot", "Jane Doe"));

        var candidate = ScCandidateMapper.ToCandidateRow(row, GeneralElection(), "https://example.com/search", FieldMap());

        Assert.Null(candidate.Email);
        Assert.Null(candidate.Phone);
        Assert.Null(candidate.Website);
        Assert.Null(candidate.MailingAddressLine);
        Assert.Null(candidate.FilingDate);
    }

    [Fact]
    public void ToCandidateRow_MissingColumns_LeaveFieldsNullNotThrowing()
    {
        var row = Row(("Office", "Governor"));

        var candidate = ScCandidateMapper.ToCandidateRow(row, GeneralElection(), "https://example.com/search", FieldMap());

        Assert.Equal("", candidate.CandidateName);
        Assert.Null(candidate.Party);
        Assert.Null(candidate.County);
        Assert.Null(candidate.Status);
    }
}
