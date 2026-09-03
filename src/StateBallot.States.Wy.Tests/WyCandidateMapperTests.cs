using StateBallot.Core;

namespace StateBallot.States.Wy.Tests;

public class WyCandidateMapperTests
{
    private static Election PrimaryElection() =>
        WyCandidateMapper.ToElection("Primary", new DateOnly(2026, 8, 18), "https://example.com/candidates.csv");

    private static Election GeneralElection() =>
        WyCandidateMapper.ToElection("General", new DateOnly(2026, 11, 3), "https://example.com/candidates.csv");

    [Fact]
    public void ToElection_BuildsIdNameAndType()
    {
        var election = PrimaryElection();

        Assert.Equal("WY", election.State);
        Assert.Equal("2026-primary", election.ElectionId);
        Assert.Equal("2026 Primary Election", election.Name);
        Assert.Equal(new DateOnly(2026, 8, 18), election.ElectionDate);
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

    // Mirrors data/input/wy/candidate_field_map.json.
    private static Dictionary<string, string> FieldMap() => new()
    {
        ["Party"] = "Party Affiliation",
        ["FilingDate"] = "Date Filed",
        ["Email"] = "Email Address",
        ["CampaignPhone"] = "Campaign Telephone",
        ["Website"] = "Web Address",
        ["MailingAddressLine"] = "Mailing Address",
    };

    [Fact]
    public void ToCandidateRow_StatewideOffice_NoDistrict()
    {
        var row = Row(("Office Sought", "GOVERNOR"), ("Ballot Name", "Brent Bien"), ("Party Affiliation", "REP"));

        var candidate = WyCandidateMapper.ToCandidateRow(row, GeneralElection(), "https://example.com/candidates.csv", FieldMap());

        Assert.Equal("GOVERNOR", candidate.Office);
        Assert.Null(candidate.District);
        Assert.Null(candidate.County);
        Assert.Equal("Brent Bien", candidate.CandidateName);
        Assert.Equal("REP", candidate.Party);
    }

    [Fact]
    public void ToCandidateRow_PrimaryOfficeWithPartySuffix_StripsSuffix()
    {
        var row = Row(("Office Sought", "UNITED STATES SENATOR - REPUBLICAN"), ("Ballot Name", "Jill M Edwards"));

        var candidate = WyCandidateMapper.ToCandidateRow(row, PrimaryElection(), "https://example.com/candidates.csv", FieldMap());

        Assert.Equal("UNITED STATES SENATOR", candidate.Office);
        Assert.Null(candidate.District);
    }

    [Fact]
    public void ToCandidateRow_LegislativeDistrictWithPartySuffix_SplitsBoth()
    {
        var row = Row(("Office Sought", "STATE SENATOR 29 - REPUBLICAN"), ("Ballot Name", "Jane Doe"));

        var candidate = WyCandidateMapper.ToCandidateRow(row, PrimaryElection(), "https://example.com/candidates.csv", FieldMap());

        Assert.Equal("STATE SENATOR", candidate.Office);
        Assert.Equal("29", candidate.District);
    }

    [Fact]
    public void ToCandidateRow_GeneralLegislativeDistrict_NoPartySuffixToStrip()
    {
        var row = Row(("Office Sought", "STATE REPRESENTATIVE 01"), ("Ballot Name", "Jane Doe"));

        var candidate = WyCandidateMapper.ToCandidateRow(row, GeneralElection(), "https://example.com/candidates.csv", FieldMap());

        Assert.Equal("STATE REPRESENTATIVE", candidate.Office);
        Assert.Equal("01", candidate.District);
    }

    [Fact]
    public void ToCandidateRow_JudicialCode_DistrictIsCodeOfficeIsDescription()
    {
        var row = Row(
            ("Office Sought", "DC-05 - JUDGE A OF THE DISTRICT COURT OF THE FIFTH JUDICIAL DISTRICT"),
            ("Ballot Name", "Bobbi Overfield"));

        var candidate = WyCandidateMapper.ToCandidateRow(row, GeneralElection(), "https://example.com/candidates.csv", FieldMap());

        Assert.Equal("JUDGE A OF THE DISTRICT COURT OF THE FIFTH JUDICIAL DISTRICT", candidate.Office);
        Assert.Equal("DC-05", candidate.District);
    }

    [Fact]
    public void ToCandidateRow_MapsAddressEmailWebsiteAndCampaignPhone()
    {
        var row = Row(
            ("Office Sought", "GOVERNOR"),
            ("Ballot Name", "Jane Doe"),
            ("Mailing Address", "P.O. BOX 96"),
            ("Mailing City State & Zip", "CODY WY 82414"),
            ("Campaign Telephone", "307-763-3442"),
            ("Email Address", "jane@example.com"),
            ("Web Address", "JANEFORWYOMING.COM"));

        var candidate = WyCandidateMapper.ToCandidateRow(row, GeneralElection(), "https://example.com/candidates.csv", FieldMap());

        Assert.Equal("P.O. BOX 96", candidate.MailingAddressLine);
        Assert.Equal("CODY", candidate.MailingCity);
        Assert.Equal("WY", candidate.MailingState);
        Assert.Equal("82414", candidate.MailingZip);
        Assert.Equal("307-763-3442", candidate.CampaignPhone);
        Assert.Null(candidate.Phone);
        Assert.Equal("jane@example.com", candidate.Email);
        Assert.Equal("JANEFORWYOMING.COM", candidate.Website);
    }

    [Fact]
    public void ToCandidateRow_DateWithdrawn_BuildsStatusString()
    {
        var row = Row(("Office Sought", "GOVERNOR"), ("Ballot Name", "Jane Doe"), ("Date Withdrawn", "5/28/2026"));

        var candidate = WyCandidateMapper.ToCandidateRow(row, GeneralElection(), "https://example.com/candidates.csv", FieldMap());

        Assert.Equal("Withdrawn - 5/28/2026", candidate.Status);
    }

    [Fact]
    public void ToCandidateRow_NoDateWithdrawn_StatusIsNull()
    {
        var row = Row(("Office Sought", "GOVERNOR"), ("Ballot Name", "Jane Doe"));

        var candidate = WyCandidateMapper.ToCandidateRow(row, GeneralElection(), "https://example.com/candidates.csv", FieldMap());

        Assert.Null(candidate.Status);
    }

    [Fact]
    public void ToCandidateRow_MissingColumns_LeaveFieldsNullNotThrowing()
    {
        var row = Row(("Office Sought", "GOVERNOR"));

        var candidate = WyCandidateMapper.ToCandidateRow(row, GeneralElection(), "https://example.com/candidates.csv", FieldMap());

        Assert.Equal("", candidate.CandidateName);
        Assert.Null(candidate.Party);
        Assert.Null(candidate.Email);
        Assert.Null(candidate.CampaignPhone);
    }
}
