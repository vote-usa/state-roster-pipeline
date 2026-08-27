namespace StateBallot.States.Ca.Tests;

public class CertifiedListPdfParserTests
{
    [Fact]
    public void ToCandidateRow_SplitsOfficeAndDistrict_MarksIncumbent()
    {
        var match = CaSelectors.Default.CertListCandidateLine.Match("Aisha Wahab* Democratic");

        var row = CertifiedListPdfParser.ToCandidateRow(match, "United States Representative District 14", "https://example.com/cert.pdf", CaSelectors.Default);

        Assert.Equal("United States Representative", row.Office);
        Assert.Equal("14", row.District);
        Assert.Equal("Aisha Wahab", row.CandidateName);
        Assert.Equal("Democratic", row.Party);
        Assert.True(row.Incumbent);
        Assert.Equal("https://example.com/cert.pdf", row.SourceUrl);
    }

    [Fact]
    public void ToCandidateRow_NoIncumbentMarker_LeavesIncumbentNull()
    {
        var match = CaSelectors.Default.CertListCandidateLine.Match("Naomi Bar-Lev No Party Preference");

        var row = CertifiedListPdfParser.ToCandidateRow(match, "Governor", "https://example.com/cert.pdf", CaSelectors.Default);

        Assert.Null(row.District);
        Assert.Equal("Governor", row.Office);
        Assert.Equal("Naomi Bar-Lev", row.CandidateName);
        Assert.Equal("No Party Preference", row.Party);
        Assert.Null(row.Incumbent);
    }

    [Fact]
    public void ToCandidateRow_UnknownParty_MapsToNull()
    {
        var match = CaSelectors.Default.CertListCandidateLine.Match("Some Candidate Unknown");

        var row = CertifiedListPdfParser.ToCandidateRow(match, "Superintendent of Public Instruction", "https://example.com/cert.pdf", CaSelectors.Default);

        Assert.Null(row.Party);
    }
}
