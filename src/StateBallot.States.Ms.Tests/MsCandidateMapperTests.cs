using StateBallot.Core;
using StateBallot.States.Ms;

namespace StateBallot.States.Ms.Tests;

public class MsCandidateMapperTests
{
    private static readonly string[] DateFormats = ["MMMM d, yyyy"];

    private static Election Primary() => MsCandidateMapper.ToElection("Primary", new DateOnly(2026, 3, 10), "https://example.com/candidates.csv");
    private static Election General() => MsCandidateMapper.ToElection("General", new DateOnly(2026, 11, 3), "https://example.com/candidates.csv");

    private static Dictionary<string, string> Row(params (string Key, string Value)[] fields)
    {
        var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in fields)
            row[key] = value;
        return row;
    }

    // Mirrors data/input/ms/candidate_field_map.json.
    private static Dictionary<string, string> FieldMap() => new()
    {
        ["Party"] = "Party",
        ["FilingDate"] = "Qualifying Period",
    };

    [Fact]
    public void ToElection_PrimaryOrGeneral_IdIsYearAndType()
    {
        var election = Primary();

        Assert.Equal("MS", election.State);
        Assert.Equal("2026-primary", election.ElectionId);
        Assert.Equal("2026 Primary Election", election.Name);
        Assert.Equal(new DateOnly(2026, 3, 10), election.ElectionDate);
        Assert.Equal("Primary", election.ElectionType);
        Assert.Equal("state", election.Jurisdiction);
    }

    [Fact]
    public void ToElection_Special_IdIsKeyedByFullDate_NotJustYear()
    {
        var election = MsCandidateMapper.ToElection("Special", new DateOnly(2026, 6, 2), "https://example.com/candidates.csv");

        // Distinguishes it from a second special election later the same year,
        // which "{year}-special" alone could not.
        Assert.Equal("2026-06-02-special", election.ElectionId);
    }

    [Fact]
    public void DetermineElection_ElectionColumnPopulated_MatchesByDate()
    {
        var elections = new List<Election> { Primary(), General() };
        var row = Row(("Office", "Candidates for Mississippi Chancery Court Judge"), ("Election", "November 3, 2026"));

        var result = MsCandidateMapper.DetermineElection(row, elections, today: new DateOnly(2026, 9, 3), DateFormats);

        Assert.Equal("General", result.ElectionType);
    }

    [Fact]
    public void DetermineElection_ElectionColumnDateNotInDiscoveredElections_Throws()
    {
        var elections = new List<Election> { Primary(), General() };
        var row = Row(("Election", "January 1, 2026"));

        Assert.Throws<InvalidOperationException>(() =>
            MsCandidateMapper.DetermineElection(row, elections, today: new DateOnly(2026, 9, 3), DateFormats));
    }

    [Fact]
    public void DetermineElection_PartisanRow_BeforePrimaryDate_GoesToPrimary()
    {
        var elections = new List<Election> { Primary(), General() };
        var row = Row(("Party", "Democratic"), ("Primary Election", "March 10, 2026"), ("General Election", "November 3, 2026"));

        var result = MsCandidateMapper.DetermineElection(row, elections, today: new DateOnly(2026, 1, 15), DateFormats);

        Assert.Equal("Primary", result.ElectionType);
    }

    [Fact]
    public void DetermineElection_PartisanRow_AfterPrimaryDate_GoesToGeneral()
    {
        // Confirms the file's own behavior: after the primary has already
        // happened, remaining rows (including Independent/Libertarian, which
        // carry the same populated Primary/General columns as Democratic/
        // Republican rows) are attributed to the General, not the Party value.
        var elections = new List<Election> { Primary(), General() };
        var row = Row(("Party", "Independent"), ("Primary Election", "March 10, 2026"), ("General Election", "November 3, 2026"));

        var result = MsCandidateMapper.DetermineElection(row, elections, today: new DateOnly(2026, 9, 3), DateFormats);

        Assert.Equal("General", result.ElectionType);
    }

    [Fact]
    public void ToCandidateRow_StripsOfficePrefix()
    {
        var row = Row(("Office", "Candidates for United States Senate"), ("Candidate Name", "Cindy Hyde-Smith"), ("Party", "Republican"));

        var candidate = MsCandidateMapper.ToCandidateRow(row, General(), "https://example.com/candidates.csv", FieldMap());

        Assert.Equal("United States Senate", candidate.Office);
        Assert.Equal("Cindy Hyde-Smith", candidate.CandidateName);
        Assert.Equal("Republican", candidate.Party);
        Assert.Null(candidate.District);
        Assert.Null(candidate.County);
    }

    [Fact]
    public void ToCandidateRow_DistrictOnly_NoPlace()
    {
        var row = Row(("Office", "Candidates for United States House of Representatives"), ("Candidate Name", "Trent Kelly"), ("District", "1"), ("Place", ""));

        var candidate = MsCandidateMapper.ToCandidateRow(row, General(), "https://example.com/candidates.csv", FieldMap());

        Assert.Equal("1", candidate.District);
    }

    [Fact]
    public void ToCandidateRow_DistrictAndPlace_CombinesBoth()
    {
        var row = Row(("Office", "Candidates for Mississippi Chancery Court Judge"), ("Candidate Name", "Brad Tennison"), ("District", "1"), ("Place", "1"));

        var candidate = MsCandidateMapper.ToCandidateRow(row, General(), "https://example.com/candidates.csv", FieldMap());

        Assert.Equal("1 Place 1", candidate.District);
    }

    [Fact]
    public void ToCandidateRow_BlankParty_IsNull()
    {
        var row = Row(("Office", "Candidates for Mississippi Circuit Court Judge"), ("Candidate Name", "John R. White"), ("Party", ""));

        var candidate = MsCandidateMapper.ToCandidateRow(row, General(), "https://example.com/candidates.csv", FieldMap());

        Assert.Null(candidate.Party);
    }

    [Fact]
    public void ToCandidateRow_QualifyingPeriod_PassedThroughRawAsFilingDate()
    {
        var row = Row(("Office", "Candidates for United States Senate"), ("Candidate Name", "Scott Colom"), ("Qualifying Period", "12/01/2025 to 12/26/2025"));

        var candidate = MsCandidateMapper.ToCandidateRow(row, General(), "https://example.com/candidates.csv", FieldMap());

        Assert.Equal("12/01/2025 to 12/26/2025", candidate.FilingDate);
    }
}
