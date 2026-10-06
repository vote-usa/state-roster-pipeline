using StateBallot.Core;

namespace StateBallot.States.Mt.Tests;

public class MtCandidateMapperTests
{
    private static Election GeneralElection() =>
        MtCandidateMapper.ToElection("General", new DateOnly(2026, 11, 3), "https://example.com/candidatelist");

    [Fact]
    public void ToElection_BuildsIdNameAndType()
    {
        var election = GeneralElection();

        Assert.Equal("MT", election.State);
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

    // Mirrors data/input/mt/candidate_field_map.json.
    private static Dictionary<string, string> FieldMap() => new()
    {
        ["Party"] = "Party Preference",
        ["Status"] = "Status",
        ["FilingDate"] = "Filing Date",
        ["Phone"] = "Phone",
    };

    [Fact]
    public void ToCandidateRow_Statewide_NoDistrict()
    {
        var row = Row(("Race", "UNITED STATES SENATOR"), ("District Type", "Statewide"), ("District", "STATE"),
            ("Name", "Seth Bodnar"), ("Party Preference", "IND"));

        var candidate = MtCandidateMapper.ToCandidateRow(row, GeneralElection(), "https://example.com/export", FieldMap());

        Assert.Equal("UNITED STATES SENATOR", candidate.Office);
        Assert.Null(candidate.District);
        Assert.Equal("Seth Bodnar", candidate.CandidateName);
        Assert.Equal("IND", candidate.Party);
    }

    [Fact]
    public void ToCandidateRow_Congressional_DistrictFromDistrictColumnNotRace()
    {
        // Race is the bare "UNITED STATES REPRESENTATIVE" for every district -
        // the number only lives in the District column.
        var row = Row(("Race", "UNITED STATES REPRESENTATIVE"), ("District Type", "Congressional"), ("District", "2ND CONGRESSIONAL"),
            ("Name", "Jane Doe"));

        var candidate = MtCandidateMapper.ToCandidateRow(row, GeneralElection(), "https://example.com/export", FieldMap());

        Assert.Equal("UNITED STATES REPRESENTATIVE", candidate.Office);
        Assert.Equal("2", candidate.District);
    }

    [Fact]
    public void ToCandidateRow_StateHouse_SplitsFromRace()
    {
        var row = Row(("Race", "STATE REPRESENTATIVE DISTRICT 12"), ("District Type", "House"), ("District", "HOUSE DISTRICT 12"),
            ("Name", "Jane Doe"));

        var candidate = MtCandidateMapper.ToCandidateRow(row, GeneralElection(), "https://example.com/export", FieldMap());

        Assert.Equal("STATE REPRESENTATIVE", candidate.Office);
        Assert.Equal("12", candidate.District);
    }

    [Fact]
    public void ToCandidateRow_PublicServiceCommission_CommaVariantSplits()
    {
        var row = Row(("Race", "PUBLIC SERVICE COMMISSIONER, DISTRICT 1"), ("District Type", "Public Service Commission"),
            ("District", "PUBLIC SERVICE COMMISSIONER DISTRICT 1"), ("Name", "Jane Doe"));

        var candidate = MtCandidateMapper.ToCandidateRow(row, GeneralElection(), "https://example.com/export", FieldMap());

        Assert.Equal("PUBLIC SERVICE COMMISSIONER", candidate.Office);
        Assert.Equal("1", candidate.District);
    }

    [Fact]
    public void ToCandidateRow_SupremeCourtJustice_SeatNumberFromHash()
    {
        var row = Row(("Race", "SUPREME COURT JUSTICE #4"), ("District Type", "Supreme Court Justice"),
            ("District", "SUPREME COURT JUSTICE"), ("Name", "Jane Doe"));

        var candidate = MtCandidateMapper.ToCandidateRow(row, GeneralElection(), "https://example.com/export", FieldMap());

        Assert.Equal("SUPREME COURT JUSTICE", candidate.Office);
        Assert.Equal("4", candidate.District);
    }

    [Fact]
    public void ToCandidateRow_JudicialWithDept_KeepsDeptInDistrict()
    {
        var row = Row(("Race", "DISTRICT COURT JUDGE DISTRICT 13, DEPT 4"), ("District Type", "Judicial"),
            ("District", "JUDICIAL DISTRICT 13"), ("Name", "Jane Doe"));

        var candidate = MtCandidateMapper.ToCandidateRow(row, GeneralElection(), "https://example.com/export", FieldMap());

        Assert.Equal("DISTRICT COURT JUDGE", candidate.Office);
        Assert.Equal("13, DEPT 4", candidate.District);
    }

    [Fact]
    public void ToCandidateRow_JudicialUnexpiredTerm_KeptInDistrict()
    {
        var row = Row(("Race", "DISTRICT COURT JUDGE DISTRICT 20, DEPT 2 UNEXPIRED"), ("District Type", "Judicial"),
            ("District", "JUDICIAL DISTRICT 20"), ("Name", "Jane Doe"));

        var candidate = MtCandidateMapper.ToCandidateRow(row, GeneralElection(), "https://example.com/export", FieldMap());

        Assert.Equal("DISTRICT COURT JUDGE", candidate.Office);
        Assert.Equal("20, DEPT 2 UNEXPIRED", candidate.District);
    }

    [Fact]
    public void ToCandidateRow_EmailAndWebsite_SplitOnBrTag()
    {
        var row = Row(("Race", "GOVERNOR"), ("District Type", "Statewide"), ("Name", "Jane Doe"),
            ("Email/Web Address", "INFO@JANEFORMT.COM<br />WWW.JANEFORMT.COM"));

        var candidate = MtCandidateMapper.ToCandidateRow(row, GeneralElection(), "https://example.com/export", FieldMap());

        Assert.Equal("INFO@JANEFORMT.COM", candidate.Email);
        Assert.Equal("WWW.JANEFORMT.COM", candidate.Website);
    }

    [Theory]
    [InlineData("Not Provided")]
    [InlineData("WRITE-IN")]
    public void ToCandidateRow_WebsitePlaceholder_MapsToNull(string placeholder)
    {
        var row = Row(("Race", "GOVERNOR"), ("District Type", "Statewide"), ("Name", "Jane Doe"),
            ("Email/Web Address", $"INFO@JANEFORMT.COM<br />{placeholder}"));

        var candidate = MtCandidateMapper.ToCandidateRow(row, GeneralElection(), "https://example.com/export", FieldMap());

        Assert.Equal("INFO@JANEFORMT.COM", candidate.Email);
        Assert.Null(candidate.Website);
    }

    [Fact]
    public void ToCandidateRow_MailingAddress_FullFourPart_SplitsAll()
    {
        var row = Row(("Race", "GOVERNOR"), ("District Type", "Statewide"), ("Name", "Jane Doe"),
            ("Mailing Address", "PO BOX 7188, MISSOULA, MT, 59807"));

        var candidate = MtCandidateMapper.ToCandidateRow(row, GeneralElection(), "https://example.com/export", FieldMap());

        Assert.Equal("PO BOX 7188", candidate.MailingAddressLine);
        Assert.Equal("MISSOULA", candidate.MailingCity);
        Assert.Equal("MT", candidate.MailingState);
        Assert.Equal("59807", candidate.MailingZip);
    }

    [Fact]
    public void ToCandidateRow_MailingAddress_NoState_StillFindsCity()
    {
        var row = Row(("Race", "GOVERNOR"), ("District Type", "Statewide"), ("Name", "Jane Doe"),
            ("Mailing Address", "3405 NORTH AVE W, MISSOULA, 59804"));

        var candidate = MtCandidateMapper.ToCandidateRow(row, GeneralElection(), "https://example.com/export", FieldMap());

        Assert.Equal("3405 NORTH AVE W", candidate.MailingAddressLine);
        Assert.Equal("MISSOULA", candidate.MailingCity);
        Assert.Null(candidate.MailingState);
    }

    [Fact]
    public void ToCandidateRow_MailingAddress_NoCity_DoesNotMisreadStreetAsCity()
    {
        // Only one part left after state+zip are removed - that's the street
        // with the city dropped by the source, not a city with the street dropped.
        var row = Row(("Race", "GOVERNOR"), ("District Type", "Statewide"), ("Name", "Jane Doe"),
            ("Mailing Address", "31 WAVING GRASS WAY, MT, 59912"));

        var candidate = MtCandidateMapper.ToCandidateRow(row, GeneralElection(), "https://example.com/export", FieldMap());

        Assert.Equal("31 WAVING GRASS WAY", candidate.MailingAddressLine);
        Assert.Null(candidate.MailingCity);
        Assert.Equal("MT", candidate.MailingState);
    }

    [Fact]
    public void ToCandidateRow_MapsStatusAndFilingDateVerbatim()
    {
        var row = Row(("Race", "GOVERNOR"), ("District Type", "Statewide"), ("Name", "Jane Doe"),
            ("Status", "FILED"), ("Filing Date", "7/10/2026 11:34:02 AM"));

        var candidate = MtCandidateMapper.ToCandidateRow(row, GeneralElection(), "https://example.com/export", FieldMap());

        Assert.Equal("FILED", candidate.Status);
        Assert.Equal("7/10/2026 11:34:02 AM", candidate.FilingDate);
    }

    [Fact]
    public void ToCandidateRow_MissingColumns_LeaveFieldsNullNotThrowing()
    {
        var row = Row(("Race", "GOVERNOR"), ("District Type", "Statewide"));

        var candidate = MtCandidateMapper.ToCandidateRow(row, GeneralElection(), "https://example.com/export", FieldMap());

        Assert.Equal("", candidate.CandidateName);
        Assert.Null(candidate.Party);
        Assert.Null(candidate.Email);
        Assert.Null(candidate.MailingAddressLine);
        Assert.Null(candidate.County);
    }
}
