using StateBallot.Core;

namespace StateBallot.States.Tx.Tests;

public class DeduplicationTests
{
    [Fact]
    public void RemovesDuplicates_SameNameOccupationFiledDate()
    {
        var candidates = new List<TexasCandidate>
        {
            new() { IdCandidate = 1, TxFullNameBallot = "JOHN DOE", TxOccupation = "ATTORNEY", DtFiled = "2026-01-15" },
            new() { IdCandidate = 2, TxFullNameBallot = "JOHN DOE", TxOccupation = "ATTORNEY", DtFiled = "2026-01-15" },
            new() { IdCandidate = 3, TxFullNameBallot = "JANE SMITH", TxOccupation = "TEACHER", DtFiled = "2026-01-20" },
        };

        var result = Deduplicator.RemoveDuplicates(candidates, TxCandidateMapper.DeduplicationKey);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, c => c.TxFullNameBallot == "JOHN DOE");
        Assert.Contains(result, c => c.TxFullNameBallot == "JANE SMITH");
    }

    [Fact]
    public void KeepsUnique_ByOccupation()
    {
        var candidates = new List<TexasCandidate>
        {
            new() { TxFullNameBallot = "JOHN DOE", TxOccupation = "ATTORNEY", DtFiled = "2026-01-15" },
            new() { TxFullNameBallot = "JOHN DOE", TxOccupation = "TEACHER", DtFiled = "2026-01-15" },
        };

        var result = Deduplicator.RemoveDuplicates(candidates, TxCandidateMapper.DeduplicationKey);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void HandlesCaseInsensitive()
    {
        var candidates = new List<TexasCandidate>
        {
            new() { TxFullNameBallot = "JOHN DOE", TxOccupation = "ATTORNEY", DtFiled = "2026-01-15" },
            new() { TxFullNameBallot = "john doe", TxOccupation = "attorney", DtFiled = "2026-01-15" },
        };

        var result = Deduplicator.RemoveDuplicates(candidates, TxCandidateMapper.DeduplicationKey);

        Assert.Single(result);
    }

    [Fact]
    public void HandlesNullValues()
    {
        var candidates = new List<TexasCandidate>
        {
            new() { TxFullNameBallot = "JOHN DOE", TxOccupation = null, DtFiled = null },
            new() { TxFullNameBallot = "JOHN DOE", TxOccupation = null, DtFiled = null },
        };

        var result = Deduplicator.RemoveDuplicates(candidates, TxCandidateMapper.DeduplicationKey);

        Assert.Single(result);
    }
}
