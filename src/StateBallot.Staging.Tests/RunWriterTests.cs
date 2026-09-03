using StateBallot.Core.Output;
using StateBallot.Staging;

namespace StateBallot.Staging.Tests;

public class RunWriterTests
{
    private static (ElectionOut, int) E(int id, string date, string type) =>
        (new ElectionOut { ElectionDate = date, ElectionType = type }, id);

    [Fact]
    public void LinkElection_MatchesDateAndType()
    {
        var elections = new[] { E(1, "2026-05-12", "Primary"), E(2, "2026-11-03", "General") };

        Assert.Equal(2, RunWriter.LinkElection(elections, "2026-11-03", "General"));
        Assert.Equal(2, RunWriter.LinkElection(elections, "2026-11-03", "general"));
    }

    [Fact]
    public void LinkElection_SameDateDifferentTypes_UsesType()
    {
        var elections = new[] { E(1, "2025-02-11", "Special"), E(2, "2025-02-11", "Conservation") };

        Assert.Equal(2, RunWriter.LinkElection(elections, "2025-02-11", "Conservation"));
        Assert.Null(RunWriter.LinkElection(elections, "2025-02-11", null)); // ambiguous by date alone
    }

    [Fact]
    public void LinkElection_NoTypeGiven_LinksWhenDateIsUnique()
    {
        var elections = new[] { E(1, "2026-05-12", "Primary"), E(2, "2026-11-03", "General") };

        Assert.Equal(1, RunWriter.LinkElection(elections, "2026-05-12", null));
    }

    [Fact]
    public void LinkElection_UnknownDate_ReturnsNull()
    {
        var elections = new[] { E(1, "2026-05-12", "Primary") };

        Assert.Null(RunWriter.LinkElection(elections, "2027-01-01", "Primary"));
    }
}
