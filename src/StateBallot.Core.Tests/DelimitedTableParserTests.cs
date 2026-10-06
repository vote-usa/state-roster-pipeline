namespace StateBallot.Core.Tests;

public class DelimitedTableParserTests
{
    [Fact]
    public void Parse_Csv_ReturnsRowsKeyedByHeader()
    {
        var text = "Name,Party,District\nJane Doe,Democratic,4\nJohn Smith,Republican,7\n";

        var rows = DelimitedTableParser.Parse(text);

        Assert.Equal(2, rows.Count);
        Assert.Equal("Jane Doe", rows[0]["Name"]);
        Assert.Equal("Democratic", rows[0]["Party"]);
        Assert.Equal("4", rows[0]["District"]);
        Assert.Equal("John Smith", rows[1]["Name"]);
    }

    [Fact]
    public void Parse_HeaderLookupIsCaseInsensitive()
    {
        var text = "NAME,party\nJane Doe,Democratic\n";

        var rows = DelimitedTableParser.Parse(text);

        Assert.Equal("Jane Doe", rows[0]["name"]);
        Assert.Equal("Democratic", rows[0]["PARTY"]);
    }

    [Fact]
    public void Parse_Tsv_UsesTabDelimiter()
    {
        var text = "Name\tParty\nJane Doe\tDemocratic\n";

        var rows = DelimitedTableParser.Parse(text, '\t');

        Assert.Single(rows);
        Assert.Equal("Jane Doe", rows[0]["Name"]);
        Assert.Equal("Democratic", rows[0]["Party"]);
    }

    [Fact]
    public void Parse_HeaderOnly_ReturnsEmptyList()
    {
        var rows = DelimitedTableParser.Parse("Name,Party\n");

        Assert.Empty(rows);
    }

    [Fact]
    public void Parse_EmptyInput_ReturnsEmptyListNotNull()
    {
        var rows = DelimitedTableParser.Parse("");

        Assert.NotNull(rows);
        Assert.Empty(rows);
    }

    [Fact]
    public void Parse_MissingFieldOnShortRow_LeavesEmptyStringRatherThanThrowing()
    {
        var text = "Name,Party,District\nJane Doe,Democratic\n";

        var rows = DelimitedTableParser.Parse(text);

        Assert.Single(rows);
        Assert.Equal("Jane Doe", rows[0]["Name"]);
        Assert.Equal("", rows[0]["District"]);
    }
}
