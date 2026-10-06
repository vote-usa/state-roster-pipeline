namespace StateBallot.Core.Tests;

public class CandidateFieldMapperTests
{
    [Fact]
    public void Get_MappedColumnHasValue_ReturnsTrimmedValue()
    {
        var fieldMap = new Dictionary<string, string> { ["Party"] = "Party Affiliation" };
        var row = new Dictionary<string, string> { ["Party Affiliation"] = "  Republican  " };

        var result = CandidateFieldMapper.Get(fieldMap, row, "Party");

        Assert.Equal("Republican", result);
    }

    [Fact]
    public void Get_FieldNotInMap_ReturnsNull()
    {
        var fieldMap = new Dictionary<string, string>();
        var row = new Dictionary<string, string> { ["Party Affiliation"] = "Republican" };

        var result = CandidateFieldMapper.Get(fieldMap, row, "Party");

        Assert.Null(result);
    }

    [Fact]
    public void Get_MappedColumnMissingFromRow_ReturnsNull()
    {
        var fieldMap = new Dictionary<string, string> { ["Party"] = "Party Affiliation" };
        var row = new Dictionary<string, string>();

        var result = CandidateFieldMapper.Get(fieldMap, row, "Party");

        Assert.Null(result);
    }

    [Fact]
    public void Get_MappedColumnIsBlank_ReturnsNull()
    {
        var fieldMap = new Dictionary<string, string> { ["Party"] = "Party Affiliation" };
        var row = new Dictionary<string, string> { ["Party Affiliation"] = "   " };

        var result = CandidateFieldMapper.Get(fieldMap, row, "Party");

        Assert.Null(result);
    }
}
