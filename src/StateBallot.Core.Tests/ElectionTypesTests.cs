using StateBallot.Core;

namespace StateBallot.Core.Tests;

public class ElectionTypesTests
{
    [Theory]
    [InlineData("General", "General")]
    [InlineData("GE", "General")]
    [InlineData("GENERAL", "General")]
    [InlineData("PRIMARY", "Primary")]
    [InlineData("P", "Primary")]
    [InlineData("Special", "Special")]
    [InlineData("Special Primary", "Special Primary")]
    [InlineData("SPECIAL GENERAL ELECTION", "Special General")]
    [InlineData("Special Election", "Special")]
    [InlineData("Primary Runoff", "Primary Runoff")]
    [InlineData("RUNOFF", "General Runoff")]
    [InlineData("Q", "Primary Runoff")]
    [InlineData("NON-PARTISAN", "Other")]
    [InlineData("Conservation", "Other")]
    [InlineData("", "Other")]
    [InlineData(null, "Other")]
    public void Normalize_MapsSourceValuesToVocabulary(string? raw, string expected)
    {
        Assert.Equal(expected, ElectionTypes.Normalize(raw));
    }

    [Fact]
    public void Vocabulary_MatchesSchemaEnum()
    {
        var schema = System.Text.Json.JsonDocument.Parse(
            File.ReadAllText(Path.Combine(Publishing.SchemaValidator.FindSchemaDir(), "common.schema.json")));
        var values = schema.RootElement.GetProperty("$defs").GetProperty("electionType").GetProperty("enum")
            .EnumerateArray().Select(v => v.GetString()).ToArray();

        Assert.Equal(ElectionTypes.All, values);
    }
}
