using StateBallot.Staging;
using Xunit;

namespace StateBallot.Staging.Tests;

public class CandidateTargetColumnsTests
{
    [Fact]
    public void Catalog_CoversEveryCandidateRowField()
    {
        // Reflection over CandidateRow drives both the catalog and this expectation, so the
        // test guards the derivation, not a hand-maintained list.
        var expected = typeof(StateBallot.Core.CandidateRow)
            .GetProperties()
            .Select(p => p.Name)
            .OrderBy(n => n)
            .ToArray();

        var actual = CandidateTargetColumns.All.Select(c => c.Name).OrderBy(n => n).ToArray();

        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("State")]
    [InlineData("ElectionDate")]
    [InlineData("ElectionType")]
    [InlineData("Office")]
    [InlineData("CandidateName")]
    [InlineData("SourceUrl")]
    public void NonNullableFields_AreRequired(string name)
    {
        var column = CandidateTargetColumns.All.Single(c => c.Name == name);
        Assert.True(column.Required);
    }

    [Theory]
    [InlineData("District")]
    [InlineData("Party")]
    [InlineData("Incumbent")]
    [InlineData("FirstName")]
    public void NullableFields_AreOptional(string name)
    {
        var column = CandidateTargetColumns.All.Single(c => c.Name == name);
        Assert.False(column.Required);
    }

    [Fact]
    public void Contains_IsCaseSensitiveToTheModel()
    {
        Assert.True(CandidateTargetColumns.Contains("CandidateName"));
        Assert.False(CandidateTargetColumns.Contains("candidatename"));
        Assert.False(CandidateTargetColumns.Contains("NotAColumn"));
    }
}
