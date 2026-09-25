using StateBallot.Core;

namespace StateBallot.States.Sd.Tests;

public class SdCandidateMapperTests
{
    private static Election GeneralElection() =>
        SdCandidateMapper.ToElection("General", new DateOnly(2026, 11, 3), "https://example.com/calendar");

    [Fact]
    public void ToElection_BuildsIdNameAndType()
    {
        var election = GeneralElection();

        Assert.Equal("SD", election.State);
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

    // Mirrors data/input/sd/candidate_field_map.json.
    private static Dictionary<string, string> FieldMap() => new()
    {
        ["Party"] = "Party",
        ["FilingDate"] = "Petition Filing Date",
    };

    [Fact]
    public void ToCandidateRow_StatewideOffice_NoDistrictOrCounty()
    {
        var row = Row(("Contest", "United States Senator"), ("District/County", ""), ("Name", "Mike Rounds"), ("Party", "REP"));

        var candidate = SdCandidateMapper.ToCandidateRow(row, GeneralElection(), "https://example.com/export", FieldMap());

        Assert.Equal("United States Senator", candidate.Office);
        Assert.Null(candidate.District);
        Assert.Null(candidate.County);
        Assert.Equal("Mike Rounds", candidate.CandidateName);
        Assert.Equal("REP", candidate.Party);
    }

    [Fact]
    public void ToCandidateRow_StateSenateDistrict_ZeroPaddedNumberNormalized()
    {
        var row = Row(("Contest", "State Senator"), ("District/County", "District 01"), ("Name", "Jane Doe"));

        var candidate = SdCandidateMapper.ToCandidateRow(row, GeneralElection(), "https://example.com/export", FieldMap());

        Assert.Equal("1", candidate.District);
        Assert.Null(candidate.County);
    }

    [Fact]
    public void ToCandidateRow_Sheriff_BareCounty_NoDistrict()
    {
        var row = Row(("Contest", "Sheriff"), ("District/County", "Aurora"), ("Name", "Jane Doe"));

        var candidate = SdCandidateMapper.ToCandidateRow(row, GeneralElection(), "https://example.com/export", FieldMap());

        Assert.Null(candidate.District);
        Assert.Equal("Aurora", candidate.County);
    }

    [Fact]
    public void ToCandidateRow_ConservationDistrictSupervisor_CountySuffixStripped()
    {
        var row = Row(("Contest", "Conservation District Supervisor"), ("District/County", "Beadle County"), ("Name", "Jane Doe"));

        var candidate = SdCandidateMapper.ToCandidateRow(row, GeneralElection(), "https://example.com/export", FieldMap());

        Assert.Equal("Beadle", candidate.County);
    }

    [Fact]
    public void ToCandidateRow_ConservationDistrictSupervisor_HyphenatedCountyPair_NotMisreadAsSubDistrict()
    {
        // "Brule-Buffalo" is two county names joined by a hyphen, not a "{county}-{n}" sub-district -
        // the trailing part isn't numeric, so this must NOT split.
        var row = Row(("Contest", "Conservation District Supervisor"), ("District/County", "Brule-Buffalo"), ("Name", "Jane Doe"));

        var candidate = SdCandidateMapper.ToCandidateRow(row, GeneralElection(), "https://example.com/export", FieldMap());

        Assert.Null(candidate.District);
        Assert.Equal("Brule-Buffalo", candidate.County);
    }

    [Fact]
    public void ToCandidateRow_CountyCommissioner_SplitsCountyAndSubDistrict()
    {
        var row = Row(("Contest", "County Commissioner"), ("District/County", "Aurora-1"), ("Name", "Jane Doe"));

        var candidate = SdCandidateMapper.ToCandidateRow(row, GeneralElection(), "https://example.com/export", FieldMap());

        Assert.Equal("1", candidate.District);
        Assert.Equal("Aurora", candidate.County);
    }

    [Fact]
    public void ToCandidateRow_CountyCommissioner_SpelledOutDistrictLabel_StillSplits()
    {
        var row = Row(("Contest", "County Commissioner"), ("District/County", "Deuel - District 1"), ("Name", "Jane Doe"));

        var candidate = SdCandidateMapper.ToCandidateRow(row, GeneralElection(), "https://example.com/export", FieldMap());

        Assert.Equal("1", candidate.District);
        Assert.Equal("Deuel", candidate.County);
    }

    [Fact]
    public void ToCandidateRow_CountyCommissioner_CompoundMultiDistrictSeat_KeptWholeAsCounty()
    {
        // "1-2" isn't a single bare number - no honest split exists, so the whole
        // value stays in County rather than guessing at one sub-district.
        var row = Row(("Contest", "County Commissioner"), ("District/County", "Lyman - District 1-2"), ("Name", "Jane Doe"));

        var candidate = SdCandidateMapper.ToCandidateRow(row, GeneralElection(), "https://example.com/export", FieldMap());

        Assert.Null(candidate.District);
        Assert.Equal("Lyman - District 1-2", candidate.County);
    }

    [Fact]
    public void ToCandidateRow_PrecinctCommitteeman_HyphenatedPrecinctLabel_Splits()
    {
        var row = Row(("Contest", "Precinct Committeeman"), ("District/County", "Aurora - Precinct-2"), ("Name", "Jane Doe"));

        var candidate = SdCandidateMapper.ToCandidateRow(row, GeneralElection(), "https://example.com/export", FieldMap());

        Assert.Equal("2", candidate.District);
        Assert.Equal("Aurora", candidate.County);
    }

    [Fact]
    public void ToCandidateRow_DelegatesToStateConvention_BareCounty()
    {
        var row = Row(("Contest", "Delegates to State Convention"), ("District/County", "Beadle"), ("Name", "Jane Doe"));

        var candidate = SdCandidateMapper.ToCandidateRow(row, GeneralElection(), "https://example.com/export", FieldMap());

        Assert.Null(candidate.District);
        Assert.Equal("Beadle", candidate.County);
    }

    [Fact]
    public void ToCandidateRow_CountyFinanceOfficer_BareCounty()
    {
        var row = Row(("Contest", "County Finance Officer"), ("District/County", "Brookings"), ("Name", "Jane Doe"));

        var candidate = SdCandidateMapper.ToCandidateRow(row, GeneralElection(), "https://example.com/export", FieldMap());

        Assert.Null(candidate.District);
        Assert.Equal("Brookings", candidate.County);
    }

    [Fact]
    public void ToCandidateRow_Alderman_WardShape_KeptVerbatimNotCounty()
    {
        // Alderman isn't in CountyOffices - "Baltic Ward-1" is a city/ward, not a county,
        // even though it has the same trailing "-N" shape County Commissioner uses.
        var row = Row(("Contest", "Alderman"), ("District/County", "Baltic Ward-1"), ("Name", "Jane Doe"));

        var candidate = SdCandidateMapper.ToCandidateRow(row, GeneralElection(), "https://example.com/export", FieldMap());

        Assert.Equal("Baltic Ward-1", candidate.District);
        Assert.Null(candidate.County);
    }

    [Fact]
    public void ToCandidateRow_WaterDistrictDirector_KeptVerbatimNotCounty()
    {
        var row = Row(("Contest", "East Dakota Water Development District Director"), ("District/County", "East Dakota WDD 1"), ("Name", "Jane Doe"));

        var candidate = SdCandidateMapper.ToCandidateRow(row, GeneralElection(), "https://example.com/export", FieldMap());

        Assert.Equal("East Dakota WDD 1", candidate.District);
        Assert.Null(candidate.County);
    }

    [Fact]
    public void ToCandidateRow_MailingAddress_SplitsTrailingStateAndZip()
    {
        var row = Row(("Contest", "Governor"), ("District/County", ""), ("Name", "Jane Doe"),
            ("Mailing Address", "2604 S Kierra Ct Sioux Falls SD 57106-5008"));

        var candidate = SdCandidateMapper.ToCandidateRow(row, GeneralElection(), "https://example.com/export", FieldMap());

        Assert.Equal("2604 S Kierra Ct Sioux Falls", candidate.MailingAddressLine);
        Assert.Equal("SD", candidate.MailingState);
        Assert.Equal("57106-5008", candidate.MailingZip);
        Assert.Null(candidate.MailingCity);
    }

    [Fact]
    public void ToCandidateRow_MalformedZip_KeepsRawAddressStateZipNull()
    {
        var row = Row(("Contest", "Governor"), ("District/County", ""), ("Name", "Jane Doe"),
            ("Mailing Address", "46224 Oakwood St Estelline SD 572.34"));

        var candidate = SdCandidateMapper.ToCandidateRow(row, GeneralElection(), "https://example.com/export", FieldMap());

        Assert.Equal("46224 Oakwood St Estelline SD 572.34", candidate.MailingAddressLine);
        Assert.Null(candidate.MailingState);
        Assert.Null(candidate.MailingZip);
    }

    [Fact]
    public void ToCandidateRow_MapsFilingDateVerbatim()
    {
        var row = Row(("Contest", "Governor"), ("District/County", ""), ("Name", "Jane Doe"),
            ("Petition Filing Date", "4/4/2026 12:00:00 AM"));

        var candidate = SdCandidateMapper.ToCandidateRow(row, GeneralElection(), "https://example.com/export", FieldMap());

        Assert.Equal("4/4/2026 12:00:00 AM", candidate.FilingDate);
    }

    [Fact]
    public void ToCandidateRow_MissingColumns_LeaveFieldsNullNotThrowing()
    {
        var row = Row(("Contest", "Governor"));

        var candidate = SdCandidateMapper.ToCandidateRow(row, GeneralElection(), "https://example.com/export", FieldMap());

        Assert.Equal("", candidate.CandidateName);
        Assert.Null(candidate.Party);
        Assert.Null(candidate.District);
        Assert.Null(candidate.County);
        Assert.Null(candidate.MailingAddressLine);
    }
}
