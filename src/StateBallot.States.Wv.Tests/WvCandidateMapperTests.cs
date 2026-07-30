using StateBallot.Core;

namespace StateBallot.States.Wv.Tests;

public class WvCandidateMapperTests
{
    [Fact]
    public void ToElection_ParsesIsoDate()
    {
        var raw = new WestVirginiaCandidate
        {
            ElectionId = 42,
            ElectionName = "2026 PRIMARY",
            ElectionDate = "2026-05-12",
            ElectionType = "PRIMARY",
        };

        var election = WvCandidateMapper.ToElection(raw);

        Assert.Equal("WV", election.State);
        Assert.Equal("42", election.ElectionId);
        Assert.Equal(new DateOnly(2026, 5, 12), election.ElectionDate);
    }

    [Fact]
    public void ToElection_UnrecognizedDateFormat_ThrowsWithElectionId()
    {
        var raw = new WestVirginiaCandidate { ElectionId = 99, ElectionDate = "not-a-date" };

        var ex = Assert.Throws<InvalidOperationException>(() => WvCandidateMapper.ToElection(raw));
        Assert.Contains("99", ex.Message);
    }

    [Fact]
    public void ToCandidateRow_UsesBallotNameWhenPresent()
    {
        var election = WvCandidateMapper.ToElection(new WestVirginiaCandidate { ElectionId = 1, ElectionDate = "2026-05-12" });
        var candidate = new WestVirginiaCandidate
        {
            CandidateId = 5,
            CandidateBallotName = "JANE Q. PUBLIC",
            CandidateFirstName = "Jane",
            CandidateLastName = "Public",
            PartyDescription = "Democrat",
            OfficeName = "Governor",
            CandidateEmail = "jane@example.com",
            CandidatePhoneNumber = "304-555-0100",
        };

        var data = WvCandidateMapper.ToCandidateRow(candidate, election, "https://example.com");

        Assert.Equal("JANE Q. PUBLIC", data.CandidateName);
        Assert.Equal("Democrat", data.Party);
        Assert.Equal("5", data.SourceCandidateId);
        Assert.Equal("jane@example.com", data.Email);
        Assert.Equal("304-555-0100", data.Phone);
    }

    [Fact]
    public void ToCandidateRow_FallsBackToNameParts_WhenNoBallotName()
    {
        var election = WvCandidateMapper.ToElection(new WestVirginiaCandidate { ElectionId = 1, ElectionDate = "2026-05-12" });
        var candidate = new WestVirginiaCandidate
        {
            CandidateFirstName = "Jane",
            CandidateMiddleName = "Q.",
            CandidateLastName = "Public",
        };

        var data = WvCandidateMapper.ToCandidateRow(candidate, election, "https://example.com");

        Assert.Equal("Jane Q. Public", data.CandidateName);
    }

    [Fact]
    public void DeduplicationKey_DistinguishesByFilingDate()
    {
        var a = new WestVirginiaCandidate { CandidateId = 1, CandidateBallotName = "X", ElectionId = 1, OfficeId = 1, FilingDate = "2026-01-01" };
        var b = new WestVirginiaCandidate { CandidateId = 1, CandidateBallotName = "X", ElectionId = 1, OfficeId = 1, FilingDate = "2026-01-02" };

        var deduped = Deduplicator.RemoveDuplicates(new[] { a, b }, WvCandidateMapper.DeduplicationKey);

        Assert.Equal(2, deduped.Count);
    }

    [Fact]
    public void DeduplicationKey_RemovesExactDuplicates()
    {
        var a = new WestVirginiaCandidate { CandidateId = 1, CandidateBallotName = "X", ElectionId = 1, OfficeId = 1, FilingDate = "2026-01-01" };
        var b = new WestVirginiaCandidate { CandidateId = 1, CandidateBallotName = "X", ElectionId = 1, OfficeId = 1, FilingDate = "2026-01-01" };

        var deduped = Deduplicator.RemoveDuplicates(new[] { a, b }, WvCandidateMapper.DeduplicationKey);

        Assert.Single(deduped);
    }
}
