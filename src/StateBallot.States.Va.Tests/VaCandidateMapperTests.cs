using StateBallot.Core;

namespace StateBallot.States.Va.Tests;

public class VaCandidateMapperTests
{
    private static Election GeneralElection() =>
        VaCandidateMapper.ToElection("General", new DateOnly(2026, 11, 3), "https://example.com/all-offices");

    [Fact]
    public void ToElection_BuildsIdNameAndType()
    {
        var election = GeneralElection();

        Assert.Equal("VA", election.State);
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

    // Mirrors data/input/va/candidate_field_map.json.
    private static Dictionary<string, string> FieldMap() => new()
    {
        ["Party"] = "Candidate Party",
        ["CampaignPhone"] = "Campaign Phone",
        ["Email"] = "Campaign Email",
        ["Website"] = "Campaign Website",
        ["MailingCity"] = "Campaign City",
        ["MailingState"] = "Campaign State",
        ["MailingZip"] = "Campaign Zip",
    };

    [Fact]
    public void ToCandidateRow_WideOffice_StatewideDistrict_NoCountyEvenWithLocality()
    {
        var row = Row(
            ("Locality", "ACCOMACK COUNTY"), ("Office Title", "Member, United States Senate"),
            ("District", "Statewide"), ("Candidate Party", "Democratic"), ("Candidate Name", "Mark R. Warner"));

        var candidate = VaCandidateMapper.ToCandidateRow(row, GeneralElection(), "https://example.com/general.xlsx", FieldMap());

        Assert.Equal("Member, United States Senate", candidate.Office);
        Assert.Null(candidate.District);
        Assert.Null(candidate.County); // wide office - Locality column ignored even though populated
        Assert.Equal("Mark R. Warner", candidate.CandidateName);
    }

    [Fact]
    public void ToCandidateRow_WideOffice_OrdinalDistrict_StrippedToBareNumber()
    {
        var row = Row(
            ("Locality", "ACCOMACK COUNTY"), ("Office Title", "Member, House of Representatives"),
            ("District", "2nd District"), ("Candidate Name", "Jen A. Kiggans"));

        var candidate = VaCandidateMapper.ToCandidateRow(row, GeneralElection(), "https://example.com/general.xlsx", FieldMap());

        Assert.Equal("2", candidate.District);
        Assert.Null(candidate.County);
    }

    [Fact]
    public void ToCandidateRow_LocalOffice_KeepsLocalityAsCounty()
    {
        var row = Row(
            ("Locality", "ACCOMACK COUNTY"), ("Office Title", "Mayor - Belle Haven"),
            ("District", "Town of Belle Haven"), ("Candidate Name", "David A. McCaleb"));

        var candidate = VaCandidateMapper.ToCandidateRow(row, GeneralElection(), "https://example.com/general.xlsx", FieldMap());

        Assert.Equal("Town of Belle Haven", candidate.District);
        Assert.Equal("ACCOMACK COUNTY", candidate.County);
    }

    [Fact]
    public void ToCandidateRow_IncumbentYes_MapsTrue()
    {
        var row = Row(("Office Title", "Governor"), ("District", "Statewide"), ("Candidate Name", "Jane Doe"), ("Incumbent", "Yes"));

        var candidate = VaCandidateMapper.ToCandidateRow(row, GeneralElection(), "https://example.com/general.xlsx", FieldMap());

        Assert.True(candidate.Incumbent);
    }

    [Fact]
    public void ToCandidateRow_IncumbentNo_MapsFalse()
    {
        var row = Row(("Office Title", "Governor"), ("District", "Statewide"), ("Candidate Name", "Jane Doe"), ("Incumbent", "No"));

        var candidate = VaCandidateMapper.ToCandidateRow(row, GeneralElection(), "https://example.com/general.xlsx", FieldMap());

        Assert.False(candidate.Incumbent);
    }

    [Fact]
    public void ToCandidateRow_JoinsBothAddressLines()
    {
        var row = Row(
            ("Office Title", "Governor"), ("District", "Statewide"), ("Candidate Name", "Jane Doe"),
            ("Campaign Address Line 1", "2111 Eisenhower Ave"), ("Campaign Address Line 2", "Ste 304"));

        var candidate = VaCandidateMapper.ToCandidateRow(row, GeneralElection(), "https://example.com/general.xlsx", FieldMap());

        Assert.Equal("2111 Eisenhower Ave, Ste 304", candidate.MailingAddressLine);
    }

    [Fact]
    public void ToCandidateRow_OnlyLine1_NoTrailingComma()
    {
        var row = Row(("Office Title", "Governor"), ("District", "Statewide"), ("Candidate Name", "Jane Doe"),
            ("Campaign Address Line 1", "PO Box 91"));

        var candidate = VaCandidateMapper.ToCandidateRow(row, GeneralElection(), "https://example.com/general.xlsx", FieldMap());

        Assert.Equal("PO Box 91", candidate.MailingAddressLine);
    }

    [Fact]
    public void ToCandidateRow_CampaignPhoneColumn_MapsToCampaignPhoneNotPhone()
    {
        var row = Row(("Office Title", "Governor"), ("District", "Statewide"), ("Candidate Name", "Jane Doe"),
            ("Campaign Phone", "703-785-8857"));

        var candidate = VaCandidateMapper.ToCandidateRow(row, GeneralElection(), "https://example.com/general.xlsx", FieldMap());

        Assert.Equal("703-785-8857", candidate.CampaignPhone);
        Assert.Null(candidate.Phone);
    }

    [Fact]
    public void MergeDuplicateLocalities_WideOffice_CollapsesToOneRow()
    {
        var rows = new List<CandidateRow>
        {
            new() { Office = "Member, United States Senate", District = null, County = null, Party = "Democratic", CandidateName = "Mark R. Warner" },
            new() { Office = "Member, United States Senate", District = null, County = null, Party = "Democratic", CandidateName = "Mark R. Warner" },
            new() { Office = "Member, United States Senate", District = null, County = null, Party = "Democratic", CandidateName = "Mark R. Warner" },
        };

        var merged = VaCandidateMapper.MergeDuplicateLocalities(rows);

        var single = Assert.Single(merged);
        Assert.Null(single.County);
    }

    [Fact]
    public void MergeDuplicateLocalities_LocalOfficeAcrossTwoCounties_JoinsWithSemicolon()
    {
        var rows = new List<CandidateRow>
        {
            new() { Office = "Mayor - Belle Haven", District = "Town of Belle Haven", County = "ACCOMACK COUNTY", Party = "Independent", CandidateName = "David A. McCaleb" },
            new() { Office = "Mayor - Belle Haven", District = "Town of Belle Haven", County = "NORTHAMPTON COUNTY", Party = "Independent", CandidateName = "David A. McCaleb" },
        };

        var merged = VaCandidateMapper.MergeDuplicateLocalities(rows);

        var single = Assert.Single(merged);
        Assert.Equal("ACCOMACK COUNTY; NORTHAMPTON COUNTY", single.County);
    }

    [Fact]
    public void MergeDuplicateLocalities_DifferentCandidatesSameOffice_StayDistinct()
    {
        var rows = new List<CandidateRow>
        {
            new() { Office = "Mayor - Belle Haven", District = "Town of Belle Haven", County = "ACCOMACK COUNTY", Party = "Independent", CandidateName = "David A. McCaleb" },
            new() { Office = "Mayor - Belle Haven", District = "Town of Belle Haven", County = "ACCOMACK COUNTY", Party = "Independent", CandidateName = "Lawrence Burr Willcox II" },
        };

        var merged = VaCandidateMapper.MergeDuplicateLocalities(rows);

        Assert.Equal(2, merged.Count);
    }

    [Fact]
    public void ToCandidateRow_MissingColumns_LeaveFieldsNullNotThrowing()
    {
        var row = Row(("Office Title", "Governor"));

        var candidate = VaCandidateMapper.ToCandidateRow(row, GeneralElection(), "https://example.com/general.xlsx", FieldMap());

        Assert.Equal("", candidate.CandidateName);
        Assert.Null(candidate.Party);
        Assert.Null(candidate.CampaignPhone);
        Assert.Null(candidate.MailingAddressLine);
    }
}
