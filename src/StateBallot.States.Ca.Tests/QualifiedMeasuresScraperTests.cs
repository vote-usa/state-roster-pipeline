using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace StateBallot.States.Ca.Tests;

public class QualifiedMeasuresScraperTests
{
    private static async Task<List<IElement>> ParagraphsAsync(string bodyHtml)
    {
        var doc = await new HtmlParser().ParseDocumentAsync($"<html><body>{bodyHtml}</body></html>");
        return doc.QuerySelectorAll("p").ToList();
    }

    [Fact]
    public async Task ToMeasureRow_InitiativeStyle_UsesBoldTitleAndTrailingSummary()
    {
        var content = await ParagraphsAsync(
            "<p><strong>Ballot Title Text.</strong> This measure concerns long-term care funding.</p>");

        var row = QualifiedMeasuresScraper.ToMeasureRow(
            "Proposition 1", new DateOnly(2026, 11, 3), content, "https://sos.ca.gov/qualified-measures");

        Assert.Equal("Proposition 1", row.MeasureId);
        Assert.Equal("2026-11-03", row.ElectionDate);
        Assert.Equal("Ballot Title Text", row.Title);
        Assert.Equal("This measure concerns long-term care funding.", row.Summary);
        Assert.Equal("state", row.Jurisdiction);
        Assert.Equal("https://sos.ca.gov/qualified-measures", row.SourceUrl);
    }

    [Fact]
    public async Task ToMeasureRow_LegislativeStyle_UsesLinkTextAndStripsPdfSuffix()
    {
        var content = await ParagraphsAsync(
            "<p><a href=\"https://leginfo.ca.gov/bill.pdf\">Assembly Bill No. 123 (PDF)</a></p>");

        var row = QualifiedMeasuresScraper.ToMeasureRow(
            "Proposition 2", new DateOnly(2026, 11, 3), content, "https://sos.ca.gov/qualified-measures");

        Assert.Equal("Assembly Bill No. 123", row.Title);
        Assert.Null(row.Summary);
        Assert.Equal("https://leginfo.ca.gov/bill.pdf", row.FullTextUrl);
    }

    [Fact]
    public async Task ToMeasureRow_NoBoldTitleOrLink_FallsBackToFirstParagraphText()
    {
        var content = await ParagraphsAsync("<p>Plain text with no bold title or link.</p>");

        var row = QualifiedMeasuresScraper.ToMeasureRow(
            "Proposition 3", new DateOnly(2026, 11, 3), content, "https://sos.ca.gov/qualified-measures");

        Assert.Equal("Plain text with no bold title or link.", row.Title);
        Assert.Null(row.Summary);
        Assert.Null(row.FullTextUrl);
    }
}
