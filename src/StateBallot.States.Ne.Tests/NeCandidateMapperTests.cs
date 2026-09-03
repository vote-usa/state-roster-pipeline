using StateBallot.Core;
using StateBallot.States.Ne;

namespace StateBallot.States.Ne.Tests;

public class NeCandidateMapperTests
{
    private static Election GeneralElection() =>
        NeCandidateMapper.ToElection("General", new DateOnly(2026, 11, 3), "https://example.com/candidates.xlsx");

    private static Dictionary<string, string> Row(params (string Key, string Value)[] fields)
    {
        var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in fields)
            row[key] = value;
        return row;
    }

    [Fact]
    public void ToElection_BuildsIdNameAndType()
    {
        var election = GeneralElection();

        Assert.Equal("NE", election.State);
        Assert.Equal("2026-general", election.ElectionId);
        Assert.Equal("2026 General Election", election.Name);
        Assert.Equal("General", election.ElectionType);
        Assert.Equal("state", election.Jurisdiction);
    }

    [Fact]
    public void ToCandidateRow_StatewideOffice_StripsForPrefix()
    {
        var row = Row(("Office", "For United States Senator"), ("Candidate Name", "Pete Ricketts"), ("Party (if applicable)", "Republican"));

        var candidate = NeCandidateMapper.ToCandidateRow(row, GeneralElection(), "https://example.com/candidates.xlsx");

        Assert.Equal("United States Senator", candidate.Office);
        Assert.Equal("Pete Ricketts", candidate.CandidateName);
        Assert.Equal("Republican", candidate.Party);
    }

    [Fact]
    public void ToCandidateRow_LocalSpecialDistrict_LeavesOfficeUnchanged()
    {
        // Doesn't start with "For " at all - the ^For\s+ anchor must not touch it.
        var row = Row(("Office", "Central Community College For Board of Governors"), ("Candidate Name", "Roger P. Davis"));

        var candidate = NeCandidateMapper.ToCandidateRow(row, GeneralElection(), "https://example.com/candidates.xlsx");

        Assert.Equal("Central Community College For Board of Governors", candidate.Office);
    }

    [Fact]
    public void ToCandidateRow_BlankParty_IsNull()
    {
        var row = Row(("Office", "For Member of the Legislature"), ("Candidate Name", "Dean Helmick"), ("Party (if applicable)", ""));

        var candidate = NeCandidateMapper.ToCandidateRow(row, GeneralElection(), "https://example.com/candidates.xlsx");

        Assert.Null(candidate.Party);
    }

    [Theory]
    [InlineData("Incumbent", true)]
    [InlineData("Nonincumbent", false)]
    [InlineData("", null)]
    public void ToCandidateRow_IncumbencyStatus_MapsToBool(string raw, bool? expected)
    {
        var row = Row(("Office", "For Governor"), ("Candidate Name", "Jim Pillen"), ("Incumbency Status", raw));

        var candidate = NeCandidateMapper.ToCandidateRow(row, GeneralElection(), "https://example.com/candidates.xlsx");

        Assert.Equal(expected, candidate.Incumbent);
    }

    [Fact]
    public void ToCandidateRow_MailingAddress_TwoLines_SplitsStreetAndCityStateZip()
    {
        var row = Row(
            ("Office", "For United States Senator"), ("Candidate Name", "Dan Osborn"),
            ("Mailing Address", "15418 Weir St., #160\nOmaha NE 68137"));

        var candidate = NeCandidateMapper.ToCandidateRow(row, GeneralElection(), "https://example.com/candidates.xlsx");

        Assert.Equal("15418 Weir St., #160", candidate.MailingAddressLine);
        Assert.Equal("Omaha", candidate.MailingCity);
        Assert.Equal("NE", candidate.MailingState);
        Assert.Equal("68137", candidate.MailingZip);
    }

    [Fact]
    public void ToCandidateRow_MailingAddress_BlankOrWhitespaceOnly_AllNull()
    {
        var row = Row(("Office", "For Attorney General"), ("Candidate Name", "Mike Hilgers"), ("Mailing Address", "\n"));

        var candidate = NeCandidateMapper.ToCandidateRow(row, GeneralElection(), "https://example.com/candidates.xlsx");

        Assert.Null(candidate.MailingAddressLine);
        Assert.Null(candidate.MailingCity);
        Assert.Null(candidate.MailingState);
        Assert.Null(candidate.MailingZip);
    }

    [Fact]
    public void ToCandidateRow_MailingAddress_LastLineDoesNotMatchCityStateZip_KeepsRawText()
    {
        var row = Row(("Office", "For Governor"), ("Candidate Name", "Someone"), ("Mailing Address", "Some unparseable text"));

        var candidate = NeCandidateMapper.ToCandidateRow(row, GeneralElection(), "https://example.com/candidates.xlsx");

        Assert.Equal("Some unparseable text", candidate.MailingAddressLine);
        Assert.Null(candidate.MailingCity);
    }

    [Fact]
    public void ToCandidateRow_PhoneAndEmail_BothPresent_SplitsByAtSign()
    {
        var row = Row(
            ("Office", "For United States Senator"), ("Candidate Name", "Pete Ricketts"),
            ("Phone/Email", "(402) 413-8595\ninfo@senatorricketts.com"));

        var candidate = NeCandidateMapper.ToCandidateRow(row, GeneralElection(), "https://example.com/candidates.xlsx");

        Assert.Equal("(402) 413-8595", candidate.Phone);
        Assert.Equal("info@senatorricketts.com", candidate.Email);
    }

    [Fact]
    public void ToCandidateRow_PhoneOnly_TrailingBlankLine_EmailIsNull()
    {
        var row = Row(("Office", "For Attorney General"), ("Candidate Name", "Mike Hilgers"), ("Phone/Email", "(402) 916-0892\n"));

        var candidate = NeCandidateMapper.ToCandidateRow(row, GeneralElection(), "https://example.com/candidates.xlsx");

        Assert.Equal("(402) 916-0892", candidate.Phone);
        Assert.Null(candidate.Email);
    }

    [Fact]
    public void ToCandidateRow_EmailOnly_NoPhoneLine_PhoneIsNull()
    {
        var row = Row(
            ("Office", "For University of Nebraska Board of Regents"), ("Candidate Name", "Jeremy Hosein"),
            ("Phone/Email", "\ninfo@drhoseinforregent.com"));

        var candidate = NeCandidateMapper.ToCandidateRow(row, GeneralElection(), "https://example.com/candidates.xlsx");

        Assert.Null(candidate.Phone);
        Assert.Equal("info@drhoseinforregent.com", candidate.Email);
    }

    [Fact]
    public void ToCandidateRow_CityOfResidence_MapsToResidentialCity()
    {
        var row = Row(("Office", "For United States Senator"), ("Candidate Name", "Pete Ricketts"), ("City of Residence", "Omaha"));

        var candidate = NeCandidateMapper.ToCandidateRow(row, GeneralElection(), "https://example.com/candidates.xlsx");

        Assert.Equal("Omaha", candidate.ResidentialCity);
    }

    [Fact]
    public void ToCandidateRow_CandidateNameWithInternalWhitespace_IsCollapsed()
    {
        var row = Row(("Office", "For Governor"), ("Candidate Name", "  Jim   Pillen  "));

        var candidate = NeCandidateMapper.ToCandidateRow(row, GeneralElection(), "https://example.com/candidates.xlsx");

        Assert.Equal("Jim Pillen", candidate.CandidateName);
    }
}
