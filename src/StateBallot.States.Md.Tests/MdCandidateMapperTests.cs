using StateBallot.Core;

namespace StateBallot.States.Md.Tests;

public class MdCandidateMapperTests
{
    private static Election Election() => MdCandidateMapper.ToElection(2026, "General", new DateOnly(2026, 11, 3), "https://example.com/elections");

    // Mirrors data/input/md/candidate_field_map.json.
    private static Dictionary<string, string> FieldMap() => new()
    {
        ["Party"] = "Office Political Party",
        ["FilingDate"] = "Filing Type and Date",
        ["Email"] = "Email",
        ["Phone"] = "Public Phone",
        ["Website"] = "Website",
        ["MailingAddressLine"] = "Campaign Mailing Address",
        ["Status"] = "Candidate Status",
    };

    [Fact]
    public void ToElection_BuildsIdNameAndType()
    {
        var election = Election();

        Assert.Equal("MD", election.State);
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

    [Fact]
    public void ToCandidateRow_StateOfMaryland_LeavesDistrictNull()
    {
        var row = Row(
            ("Office Name", "Governor / Lt. Governor"),
            ("Contest Run By District Name and Number", "State Of Maryland"),
            ("Candidate First Name and Middle Name", "Wes"),
            ("Candidate Ballot Last Name and Suffix", "Moore"),
            ("Office Political Party", "Democratic"),
            ("Candidate Status", "Active"));

        var candidate = MdCandidateMapper.ToCandidateRow(row, Election(), "https://example.com/candidates.csv", FieldMap());

        Assert.Equal("MD", candidate.State);
        Assert.Equal("2026-11-03", candidate.ElectionDate);
        Assert.Equal("General", candidate.ElectionType);
        Assert.Equal("Governor / Lt. Governor", candidate.Office);
        Assert.Null(candidate.District);
        Assert.Null(candidate.County);
        Assert.Equal("Wes Moore", candidate.CandidateName);
        Assert.Equal("Democratic", candidate.Party);
        Assert.Equal("Active", candidate.Status);
    }

    [Fact]
    public void ToCandidateRow_DistrictRace_KeepsContestValueAsDistrict()
    {
        var row = Row(
            ("Office Name", "House of Delegates"),
            ("Contest Run By District Name and Number", "Legislative District 1A"),
            ("Candidate First Name and Middle Name", "Dan"),
            ("Candidate Ballot Last Name and Suffix", "Duggan"));

        var candidate = MdCandidateMapper.ToCandidateRow(row, Election(), "https://example.com/candidates.csv", FieldMap());

        Assert.Equal("Legislative District 1A", candidate.District);
    }

    [Fact]
    public void ToCandidateRow_SplitsMailingCityStateZip()
    {
        var row = Row(
            ("Office Name", "Attorney General"),
            ("Contest Run By District Name and Number", "State Of Maryland"),
            ("Candidate First Name and Middle Name", "Anthony G."),
            ("Candidate Ballot Last Name and Suffix", "Brown"),
            ("Campaign Mailing Address", "12138 Central Avenue #671"),
            ("Campaign Mailing City State and Zip", "Bowie MD 20721"));

        var candidate = MdCandidateMapper.ToCandidateRow(row, Election(), "https://example.com/candidates.csv", FieldMap());

        Assert.Equal("12138 Central Avenue #671", candidate.MailingAddressLine);
        Assert.Equal("Bowie", candidate.MailingCity);
        Assert.Equal("MD", candidate.MailingState);
        Assert.Equal("20721", candidate.MailingZip);
    }

    [Fact]
    public void ToCandidateRow_UnparseableMailingAddress_LeavesPartsNull()
    {
        var row = Row(
            ("Office Name", "Attorney General"),
            ("Candidate First Name and Middle Name", "Jane"),
            ("Candidate Ballot Last Name and Suffix", "Doe"),
            ("Campaign Mailing City State and Zip", ""));

        var candidate = MdCandidateMapper.ToCandidateRow(row, Election(), "https://example.com/candidates.csv", FieldMap());

        Assert.Null(candidate.MailingCity);
        Assert.Null(candidate.MailingState);
        Assert.Null(candidate.MailingZip);
    }

    [Fact]
    public void ToCandidateRow_StripsCountySuffixFromResidentialJurisdiction()
    {
        var row = Row(
            ("Office Name", "Attorney General"),
            ("Candidate First Name and Middle Name", "Anthony"),
            ("Candidate Ballot Last Name and Suffix", "Brown"),
            ("Candidate Residential Jurisdiction", "Prince George's County"));

        var candidate = MdCandidateMapper.ToCandidateRow(row, Election(), "https://example.com/candidates.csv", FieldMap());

        Assert.Equal("Prince George's", candidate.ResidentialCounty);
    }

    [Fact]
    public void ToCandidateRow_ResidentialJurisdictionWithoutCountySuffix_PassesThroughUnchanged()
    {
        var row = Row(
            ("Office Name", "Comptroller"),
            ("Candidate First Name and Middle Name", "Jane"),
            ("Candidate Ballot Last Name and Suffix", "Doe"),
            ("Candidate Residential Jurisdiction", "Baltimore City"));

        var candidate = MdCandidateMapper.ToCandidateRow(row, Election(), "https://example.com/candidates.csv", FieldMap());

        Assert.Equal("Baltimore City", candidate.ResidentialCounty);
    }

    [Fact]
    public void ToCandidateRow_MissingColumns_LeaveFieldsNullNotThrowing()
    {
        var row = Row(("Office Name", "Attorney General"));

        var candidate = MdCandidateMapper.ToCandidateRow(row, Election(), "https://example.com/candidates.csv", FieldMap());

        Assert.Equal("", candidate.CandidateName);
        Assert.Null(candidate.Party);
        Assert.Null(candidate.Status);
        Assert.Null(candidate.Email);
    }

    [Fact]
    public void ToCandidateRow_FieldMapMissingKey_LeavesThatFieldNull()
    {
        var row = Row(("Office Name", "Attorney General"), ("Public Phone", "410-555-0100"));

        var candidate = MdCandidateMapper.ToCandidateRow(row, Election(), "https://example.com/candidates.csv", fieldMap: []);

        // Even though the row has a value, an empty field map means "don't look" -
        // confirms the mapping goes through candidate_field_map.json, not a hardcoded column name.
        Assert.Null(candidate.Phone);
    }
}
