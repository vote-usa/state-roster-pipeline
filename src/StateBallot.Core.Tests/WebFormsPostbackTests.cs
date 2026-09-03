namespace StateBallot.Core.Tests;

public class WebFormsPostbackTests
{
    [Fact]
    public void HiddenFields_ExtractsNameAndValue()
    {
        var html = """
            <html><body><form>
                <input type="hidden" name="__VIEWSTATE" value="abc123" />
                <input type="hidden" name="__EVENTVALIDATION" value="def456" />
            </form></body></html>
            """;

        var fields = WebFormsPostback.HiddenFields(html);

        Assert.Equal("abc123", fields["__VIEWSTATE"]);
        Assert.Equal("def456", fields["__EVENTVALIDATION"]);
        Assert.Equal(2, fields.Count);
    }

    [Fact]
    public void HiddenFields_IgnoresNonHiddenInputs()
    {
        var html = """
            <html><body><form>
                <input type="hidden" name="__VIEWSTATE" value="abc123" />
                <input type="text" name="search" value="ignored" />
                <input type="submit" name="ExportButton" value="" />
            </form></body></html>
            """;

        var fields = WebFormsPostback.HiddenFields(html);

        Assert.Single(fields);
        Assert.Equal("abc123", fields["__VIEWSTATE"]);
    }

    [Fact]
    public void HiddenFields_HandlesMissingValueAttribute()
    {
        var html = """<input type="hidden" name="__EVENTTARGET" />""";

        var fields = WebFormsPostback.HiddenFields(html);

        Assert.Equal("", fields["__EVENTTARGET"]);
    }

    [Fact]
    public void HiddenFields_WorksRegardlessOfAttributeOrder()
    {
        var html = """<input value="xyz" name="Field1" type="hidden" />""";

        var fields = WebFormsPostback.HiddenFields(html);

        Assert.Equal("xyz", fields["Field1"]);
    }

    [Fact]
    public void HiddenFields_EmptyHtml_ReturnsEmptyDictionary()
    {
        var fields = WebFormsPostback.HiddenFields("<html><body>No form here</body></html>");

        Assert.Empty(fields);
    }
}
