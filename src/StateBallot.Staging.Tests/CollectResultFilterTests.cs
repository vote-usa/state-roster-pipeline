using StateBallot.Core;
using StateBallot.Staging;

namespace StateBallot.Staging.Tests;

public class CollectResultFilterTests
{
    private static CollectResult TwoElections()
    {
        var result = new CollectResult();

        foreach (var (date, type, id) in new[] { ("2026-05-12", "Primary", "1"), ("2026-11-03", "General", "2") })
        {
            result.Elections.Add(new Election
            {
                State = "WV", ElectionId = id, Name = $"{type} {date}",
                ElectionDate = DateOnly.Parse(date), ElectionType = type,
            });
            result.Candidates.Add(new CandidateRow
            {
                State = "WV", ElectionDate = date, ElectionType = type,
                Office = "GOVERNOR", CandidateName = $"Candidate {id}",
            });
            result.Measures.Add(new MeasureRow
            {
                State = "WV", ElectionDate = date, MeasureId = $"m{id}", Title = "Levy", Jurisdiction = "Kanawha County",
            });
            result.CountyBallots.Add(new CountyBallot
            {
                State = "WV", CountyName = "Kanawha", ElectionDate = date, ElectionType = type,
            });
        }

        result.StatewideProposedMeasures.Add(new MeasureRow
        {
            State = "WV", ElectionDate = null, MeasureId = "Amendment 1", Title = "Proposed", Jurisdiction = "state",
        });
        result.CountyDirectory.Add(new CountyDirectoryRow { State = "WV", CountyName = "Kanawha" });
        result.Gaps.Add("a gap");

        return result;
    }

    [Fact]
    public void ToElectionDate_KeepsOnlyThatElection()
    {
        var filtered = CollectResultFilter.ToElectionDate(TwoElections(), "2026-11-03");

        Assert.Equal("2", Assert.Single(filtered.Elections).ElectionId);
        Assert.Equal("Candidate 2", Assert.Single(filtered.Candidates).CandidateName);
        Assert.Equal("m2", Assert.Single(filtered.Measures).MeasureId);
        Assert.Equal("2026-11-03", Assert.Single(filtered.CountyBallots).ElectionDate);
    }

    [Fact]
    public void ToElectionDate_KeepsStateLevelRows()
    {
        var filtered = CollectResultFilter.ToElectionDate(TwoElections(), "2026-11-03");

        Assert.Single(filtered.CountyDirectory);
        Assert.Equal("Amendment 1", Assert.Single(filtered.StatewideProposedMeasures).MeasureId);
        Assert.Single(filtered.Gaps);
    }

    [Fact]
    public void ToElectionDate_UnknownDate_IsEmptyOfElections()
    {
        var filtered = CollectResultFilter.ToElectionDate(TwoElections(), "2027-03-01");

        Assert.Empty(filtered.Elections);
        Assert.Empty(filtered.Candidates);
        Assert.Single(filtered.CountyDirectory);
    }

    [Fact]
    public void ToElectionDate_CarriesPendingElections()
    {
        var result = TwoElections();
        result.PendingElections.Add(result.Elections[1]);

        var filtered = CollectResultFilter.ToElectionDate(result, "2026-11-03");

        Assert.Equal("2", Assert.Single(filtered.PendingElections).ElectionId);
    }

    [Fact]
    public void ElectionDates_ListsDistinctDatesAscending()
    {
        string[] expected = ["2026-05-12", "2026-11-03"];
        Assert.Equal(expected, CollectResultFilter.ElectionDates(TwoElections()).ToArray());
    }
}
