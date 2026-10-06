using StateBallot.Core;

namespace StateBallot.States.Vt.Tests;

public class VtCandidateMapperTests
{
    private static Election PrimaryElection() =>
        VtCandidateMapper.ToElection("Primary", new DateOnly(2026, 8, 11), "https://example.com/candidates");

    private static Election GeneralElection() =>
        VtCandidateMapper.ToElection("General", new DateOnly(2026, 11, 3), "https://example.com/candidates");

    [Fact]
    public void ToElection_BuildsIdNameAndType()
    {
        var election = PrimaryElection();

        Assert.Equal("VT", election.State);
        Assert.Equal("2026-primary", election.ElectionId);
        Assert.Equal("2026 Primary Election", election.Name);
        Assert.Equal(new DateOnly(2026, 8, 11), election.ElectionDate);
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

    // Mirrors data/input/vt/candidate_field_map.json.
    private static Dictionary<string, string> FieldMap() => new()
    {
        ["Party"] = "Party",
        ["Email"] = "Email",
        ["Website"] = "Website",
        ["MailingAddressLine"] = "Address",
        ["MailingCity"] = "City",
        ["MailingState"] = "State",
        ["MailingZip"] = "Zip",
        ["ResidentialCity"] = "Town Of Residence",
    };

    [Fact]
    public void ToCandidateRow_StatewideOffice_NADistrict_NoDistrictOrCounty()
    {
        var row = Row(("Contest", "GOVERNOR"), ("District Name", "N/A"), ("Name On Ballot", "Jane Doe"), ("Party", "DEMOCRATIC"));

        var candidate = VtCandidateMapper.ToCandidateRow(row, GeneralElection(), "https://example.com/general.xlsx", FieldMap());

        Assert.Equal("GOVERNOR", candidate.Office);
        Assert.Null(candidate.District);
        Assert.Null(candidate.County);
        Assert.Equal("Jane Doe", candidate.CandidateName);
        Assert.Equal("DEMOCRATIC", candidate.Party);
    }

    [Fact]
    public void ToCandidateRow_StateSenator_CompoundCodeKeptVerbatimAsDistrict()
    {
        var row = Row(("Contest", "STATE SENATOR"), ("District Name", "CHI CT 1"), ("Name On Ballot", "Jane Doe"));

        var candidate = VtCandidateMapper.ToCandidateRow(row, GeneralElection(), "https://example.com/general.xlsx", FieldMap());

        Assert.Equal("CHI CT 1", candidate.District);
        Assert.Null(candidate.County);
    }

    [Fact]
    public void ToCandidateRow_StatesAttorney_DistrictValueBecomesCounty()
    {
        var row = Row(("Contest", "STATE'S ATTORNEY"), ("District Name", "WINDHAM"), ("Name On Ballot", "Jane Doe"));

        var candidate = VtCandidateMapper.ToCandidateRow(row, GeneralElection(), "https://example.com/general.xlsx", FieldMap());

        Assert.Null(candidate.District);
        Assert.Equal("WINDHAM", candidate.County);
    }

    [Fact]
    public void ToCandidateRow_JusticeOfThePeace_TownKeptAsDistrictNotCounty()
    {
        var row = Row(("Contest", "JUSTICE OF THE PEACE"), ("District Name", "BARNET"), ("Name On Ballot", "Jane Doe"));

        var candidate = VtCandidateMapper.ToCandidateRow(row, GeneralElection(), "https://example.com/general.xlsx", FieldMap());

        Assert.Equal("BARNET", candidate.District);
        Assert.Null(candidate.County);
    }

    [Fact]
    public void ToCandidateRow_MapsAddressEmailWebsiteAndResidentialCity()
    {
        var row = Row(
            ("Contest", "GOVERNOR"), ("District Name", "N/A"), ("Name On Ballot", "Jane Doe"),
            ("Town Of Residence", "MONTPELIER"), ("Address", "PO BOX 91"), ("City", "WATERBURY"),
            ("State", "VT"), ("Zip", "05676"), ("Email", "jane@example.com"), ("Website", "JANEFORVT.COM"));

        var candidate = VtCandidateMapper.ToCandidateRow(row, GeneralElection(), "https://example.com/general.xlsx", FieldMap());

        Assert.Equal("MONTPELIER", candidate.ResidentialCity);
        Assert.Equal("PO BOX 91", candidate.MailingAddressLine);
        Assert.Equal("WATERBURY", candidate.MailingCity);
        Assert.Equal("VT", candidate.MailingState);
        Assert.Equal("05676", candidate.MailingZip);
        Assert.Equal("jane@example.com", candidate.Email);
        Assert.Equal("JANEFORVT.COM", candidate.Website);
    }

    [Fact]
    public void ToCandidateRow_DayTimePhonePresent_UsesDayTimePhone()
    {
        var row = Row(("Contest", "GOVERNOR"), ("District Name", "N/A"), ("Name On Ballot", "Jane Doe"),
            ("Day Time Phone", "(802) 555-1111"), ("Evening Phone", "(802) 555-2222"));

        var candidate = VtCandidateMapper.ToCandidateRow(row, GeneralElection(), "https://example.com/general.xlsx", FieldMap());

        Assert.Equal("(802) 555-1111", candidate.Phone);
        Assert.Null(candidate.CampaignPhone);
    }

    [Fact]
    public void ToCandidateRow_OnlyEveningPhone_FallsBackToEveningPhone()
    {
        var row = Row(("Contest", "GOVERNOR"), ("District Name", "N/A"), ("Name On Ballot", "Jane Doe"),
            ("Evening Phone", "(802) 555-2222"));

        var candidate = VtCandidateMapper.ToCandidateRow(row, GeneralElection(), "https://example.com/general.xlsx", FieldMap());

        Assert.Equal("(802) 555-2222", candidate.Phone);
    }

    [Fact]
    public void ToCandidateRow_NoPhoneColumnsPopulated_PhoneIsNull()
    {
        var row = Row(("Contest", "GOVERNOR"), ("District Name", "N/A"), ("Name On Ballot", "Jane Doe"));

        var candidate = VtCandidateMapper.ToCandidateRow(row, GeneralElection(), "https://example.com/general.xlsx", FieldMap());

        Assert.Null(candidate.Phone);
    }

    [Fact]
    public void ToCandidateRow_MissingColumns_LeaveFieldsNullNotThrowing()
    {
        var row = Row(("Contest", "GOVERNOR"), ("District Name", "N/A"));

        var candidate = VtCandidateMapper.ToCandidateRow(row, GeneralElection(), "https://example.com/general.xlsx", FieldMap());

        Assert.Equal("", candidate.CandidateName);
        Assert.Null(candidate.Party);
        Assert.Null(candidate.Email);
        Assert.Null(candidate.Phone);
    }
}
