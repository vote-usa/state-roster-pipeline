using StateBallot.Core;

namespace StateBallot.States.Wa.Tests;

public class WaMapperTests
{
    private static Election Election(string type = "General") => new()
    {
        State = "WA",
        ElectionId = "898",
        Name = "GENERAL 2026",
        ElectionDate = new DateOnly(2026, 11, 3),
        ElectionType = type,
    };

    [Fact]
    public void ToCandidateRow_MapsFieldsAndNormalizesParty()
    {
        var category = new GuideCategory { Name = "Statewide Candidates", CategoryCode = "STW" };
        var race = new GuideRace { Name = " Governor ", Jurisdiction = null };
        var candidate = new GuideCandidate { BallotName = " Jane Q. Public ", PartyName = "(Prefers Democratic Party)" };

        var row = WaMapper.ToCandidateRow(
            "WA", Election(), category, race, candidate, county: null, sourceUrl: "https://example.com/guide",
            selectors: Selectors.Default, isStatewideCategory: true);

        Assert.Equal("WA", row.State);
        Assert.Equal("2026-11-03", row.ElectionDate);
        Assert.Equal("General", row.ElectionType);
        Assert.Equal("Governor", row.Office);
        Assert.Null(row.District);
        Assert.Null(row.County);
        Assert.Equal("Jane Q. Public", row.CandidateName);
        Assert.Equal("Democratic Party", row.Party);
        Assert.Null(row.Incumbent);
        Assert.Equal("https://example.com/guide", row.SourceUrl);
    }

    [Fact]
    public void ToCandidateRow_UsesRaceJurisdictionAsDistrict()
    {
        var category = new GuideCategory { Name = "Legislative Candidates", CategoryCode = "LEG" };
        var race = new GuideRace { Name = "State Senator", Jurisdiction = " District 5 " };
        var candidate = new GuideCandidate { BallotName = "Jane Public", PartyName = "States No Party Preference" };

        var row = WaMapper.ToCandidateRow(
            "WA", Election(), category, race, candidate, "King", "https://example.com/guide", Selectors.Default,
            isStatewideCategory: false);

        Assert.Equal("District 5", row.District);
        Assert.Equal("King", row.County);
        Assert.Equal("No Party Preference", row.Party);
    }

    [Fact]
    public void ToMeasureRow_PrefersBallotTitleOverMeasureNameAndName()
    {
        var race = new GuideRace
        {
            RaceID = "R1",
            Name = "Fallback Name",
            MeasureName = "Fallback Measure Name",
            BallotTitle = "Initiative Measure No. 2124",
            ShortDescription = "<p>Concerns long-term care.</p>",
            Jurisdiction = null,
        };

        var row = WaMapper.ToMeasureRow("WA", Election(), race, county: null, sourceUrl: "https://example.com/guide");

        Assert.Equal("R1", row.MeasureId);
        Assert.Equal("Initiative Measure No. 2124", row.Title);
        Assert.Equal("Concerns long-term care.", row.Summary);
        Assert.Equal("local", row.Jurisdiction);
    }

    [Fact]
    public void ToMeasureRow_JurisdictionUsesTrimmedRaceJurisdictionWhenPresent()
    {
        var race = new GuideRace { RaceID = "R2", Name = "Prop 1", Jurisdiction = " King County " };

        var row = WaMapper.ToMeasureRow("WA", Election(), race, "King", "https://example.com/guide");

        Assert.Equal("King County", row.Jurisdiction);
        Assert.Equal("King", row.County);
    }
}
