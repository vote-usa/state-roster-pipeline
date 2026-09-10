using StateBallot.Core;

namespace StateBallot.States.Nm.Tests;

public class NmRetentionMapperTests
{
    private static Election GeneralElection() =>
        NmCandidateMapper.ToElection("General", new DateOnly(2026, 11, 3), "https://example.com/elections");

    private static Dictionary<string, string> Row(params (string Key, string Value)[] fields)
    {
        var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in fields)
            row[key] = value;
        return row;
    }

    [Fact]
    public void ToMeasureRow_BareRetention_NoSeatDescriptor()
    {
        var row = Row(("Contest", "Judicial Retention"), ("First Name", "DAVID"), ("Middle Name", "K"), ("Last Name", "THOMSON"));

        var measure = NmRetentionMapper.ToMeasureRow(row, GeneralElection(), "https://example.com/export");

        Assert.Equal("Shall DAVID K THOMSON be retained in office?", measure.Title);
        Assert.Equal("Judicial Retention", measure.Jurisdiction);
        Assert.Null(measure.County);
        Assert.Equal("2026-11-03", measure.ElectionDate);
    }

    [Fact]
    public void ToMeasureRow_CompositeSeat_IncludesSeatInTitleAndJurisdiction()
    {
        // HtmlTableParser already reads "<br />" as a space by the time this row is built.
        var row = Row(
            ("Contest", "Judicial Retention Judge of the Metropolitan Court DIVISION 2"),
            ("Filing County", "Bernalillo"),
            ("First Name", "CHRISTINE"), ("Middle Name", "EVE"), ("Last Name", "RODRIGUEZ"));

        var measure = NmRetentionMapper.ToMeasureRow(row, GeneralElection(), "https://example.com/export");

        Assert.Equal("Shall CHRISTINE EVE RODRIGUEZ be retained in office as Judge of the Metropolitan Court DIVISION 2?", measure.Title);
        Assert.Equal("Judge of the Metropolitan Court DIVISION 2", measure.Jurisdiction);
        Assert.Equal("Bernalillo", measure.County);
    }

    [Fact]
    public void ToMeasureRow_MeasureIdIsSlugged()
    {
        var row = Row(("Contest", "Judicial Retention Judge of the Metropolitan Court DIVISION 2"),
            ("First Name", "Jane"), ("Last Name", "Doe"));

        var measure = NmRetentionMapper.ToMeasureRow(row, GeneralElection(), "https://example.com/export");

        Assert.Matches("^[a-z0-9-]+$", measure.MeasureId);
        Assert.DoesNotContain("--", measure.MeasureId);
    }
}
