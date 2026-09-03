using StateBallot.Core;
using StateBallot.States.Ne;

namespace StateBallot.States.Ne.Tests;

public class NeRetentionMapperTests
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
    public void ToMeasureRow_UsesGeneralElectionDate_RegardlessOfPrimaryStage()
    {
        var row = Row(
            ("Office", "Judge of the District Court"), ("District (if applicable)", "4"),
            ("Judge (Ballot Name)", "Molly B. Keane"), ("Ballot Question", "Shall Judge Molly B. Keane be retained in office?"));

        var measure = NeRetentionMapper.ToMeasureRow(row, GeneralElection(), "https://example.com/candidates.xlsx");

        Assert.Equal("2026-11-03", measure.ElectionDate);
    }

    [Fact]
    public void ToMeasureRow_TitleIsTheRawBallotQuestion()
    {
        var row = Row(
            ("Office", "Judge of the Appeals Court"), ("District (if applicable)", "2"),
            ("Judge (Ballot Name)", "Michael W. Pirtle"), ("Ballot Question", "Shall Judge Michael W. Pirtle be retained in office?"));

        var measure = NeRetentionMapper.ToMeasureRow(row, GeneralElection(), "https://example.com/candidates.xlsx");

        Assert.Equal("Shall Judge Michael W. Pirtle be retained in office?", measure.Title);
        Assert.Null(measure.Summary);
        Assert.Null(measure.County);
    }

    [Fact]
    public void ToMeasureRow_NumberedDistrict_JurisdictionCombinesOfficeAndDistrict()
    {
        var row = Row(
            ("Office", "Judge of the District Court"), ("District (if applicable)", "4"),
            ("Judge (Ballot Name)", "Molly B. Keane"), ("Ballot Question", "Shall Judge Molly B. Keane be retained in office?"));

        var measure = NeRetentionMapper.ToMeasureRow(row, GeneralElection(), "https://example.com/candidates.xlsx");

        Assert.Equal("Judge of the District Court, District 4", measure.Jurisdiction);
    }

    [Fact]
    public void ToMeasureRow_StatewideDistrict_JurisdictionIsOfficeOnly()
    {
        var row = Row(
            ("Office", "Judge of the Nebraska Workers' Compensation Court"), ("District (if applicable)", "Statewide"),
            ("Judge (Ballot Name)", "Thomas E. Stine"), ("Ballot Question", "Shall Judge Thomas E. Stine be retained in office?"));

        var measure = NeRetentionMapper.ToMeasureRow(row, GeneralElection(), "https://example.com/candidates.xlsx");

        Assert.Equal("Judge of the Nebraska Workers' Compensation Court", measure.Jurisdiction);
    }

    [Fact]
    public void ToMeasureRow_MeasureId_IsSlugifiedAndStable()
    {
        var row = Row(
            ("Office", "Judge of the District Court"), ("District (if applicable)", "4"),
            ("Judge (Ballot Name)", "Molly B. Keane"), ("Ballot Question", "Shall Judge Molly B. Keane be retained in office?"));

        var measure = NeRetentionMapper.ToMeasureRow(row, GeneralElection(), "https://example.com/candidates.xlsx");

        Assert.Equal("retention-judge-of-the-district-court-4-molly-b-keane", measure.MeasureId);
    }
}
