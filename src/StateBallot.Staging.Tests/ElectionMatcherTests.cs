using StateBallot.Core.Output;
using StateBallot.Staging;

namespace StateBallot.Staging.Tests;

public class ElectionMatcherTests
{
    private static ElectionOut E(string date, string type, string id = "") =>
        new() { ElectionDate = date, ElectionType = type, ElectionId = id };

    private static readonly List<ElectionOut> TwoDates = [E("2026-05-12", "Primary"), E("2026-11-03", "General")];

    // WA ran a Special and a Conservation election on the same day in 2025.
    private static readonly List<ElectionOut> SameDate = [E("2025-02-11", "Special"), E("2025-02-11", "Conservation")];

    [Fact]
    public void Find_UniqueDate_Matches()
    {
        Assert.Equal((1, null), ElectionMatcher.Find(TwoDates, "2026-11-03", "General"));
        Assert.Equal((0, null), ElectionMatcher.Find(TwoDates, "2026-05-12", null));
    }

    [Fact]
    public void Find_UniqueDate_IgnoresTypeMismatch()
    {
        // Only one election on the date, so the date alone identifies it.
        Assert.Equal((1, null), ElectionMatcher.Find(TwoDates, "2026-11-03", "Whatever"));
    }

    [Fact]
    public void Find_SharedDate_UsesTypeCaseInsensitively()
    {
        Assert.Equal((1, null), ElectionMatcher.Find(SameDate, "2025-02-11", "Conservation"));
        Assert.Equal((0, null), ElectionMatcher.Find(SameDate, "2025-02-11", "special"));
    }

    [Fact]
    public void Find_SharedDateWithoutUsableType_IsAmbiguous()
    {
        Assert.Equal((null, ElectionMatcher.AmbiguousElection), ElectionMatcher.Find(SameDate, "2025-02-11", null));
        Assert.Equal((null, ElectionMatcher.AmbiguousElection), ElectionMatcher.Find(SameDate, "2025-02-11", "Primary"));
    }

    [Fact]
    public void Find_UnknownOrMissingDate_HasNoMatch()
    {
        Assert.Equal((null, ElectionMatcher.NoMatchingElection), ElectionMatcher.Find(TwoDates, "2027-01-01", "General"));
        Assert.Equal((null, ElectionMatcher.NoMatchingElection), ElectionMatcher.Find(TwoDates, null, "General"));
        Assert.Equal((null, ElectionMatcher.NoMatchingElection), ElectionMatcher.Find(TwoDates, "  ", null));
    }

    [Fact]
    public void Group_SplitsRowsByElection()
    {
        (string Date, string Type, string Name)[] rows =
        [
            ("2026-05-12", "Primary", "a"),
            ("2026-05-12", "Primary", "b"),
            ("2026-11-03", "General", "c"),
        ];

        var grouped = ElectionMatcher.Group(TwoDates, rows, r => ((string?)r.Date, (string?)r.Type));

        Assert.Empty(grouped.Unassigned);
        string[] firstElection = ["a", "b"];
        string[] secondElection = ["c"];
        Assert.Equal(firstElection, grouped.ByElection[0].Select(r => r.Name).ToArray());
        Assert.Equal(secondElection, grouped.ByElection[1].Select(r => r.Name).ToArray());
    }

    [Fact]
    public void Group_ReportsRowsItCannotPlace()
    {
        (string Date, string Type, string Name)[] rows =
        [
            ("2026-11-03", "General", "kept"),
            ("2030-01-01", "General", "orphan"),
        ];

        var grouped = ElectionMatcher.Group(TwoDates, rows, r => ((string?)r.Date, (string?)r.Type));

        Assert.Single(grouped.ByElection[1]);
        var lost = Assert.Single(grouped.Unassigned);
        Assert.Equal("orphan", lost.Row.Name);
        Assert.Equal(ElectionMatcher.NoMatchingElection, lost.Reason);
    }

    [Fact]
    public void Group_NoElections_LeavesEverythingUnassigned()
    {
        (string Date, string Type, string Name)[] rows = [("2026-11-03", "General", "a")];

        var grouped = ElectionMatcher.Group([], rows, r => ((string?)r.Date, (string?)r.Type));

        Assert.Empty(grouped.ByElection);
        Assert.Single(grouped.Unassigned);
    }
}
