using System.Text;
using StateBallot.Core.Raw;

namespace StateBallot.States.Wv.Tests;

public sealed class WvNormalizeTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "wv-capture-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void Normalize_ReadsEveryCapturedPage()
    {
        var sink = new RawSink(_dir, "1", "WV", 2026);
        RecordPage(sink, 0, Candidate(101, 42, "2026-05-12", "PRIMARY", "JANE Q. PUBLIC"), Candidate(102, 42, "2026-05-12", "PRIMARY", "JOHN DOE"));
        RecordPage(sink, 1, Candidate(201, 43, "2026-11-03", "GENERAL", "ANN ROE"));

        var result = new WvCollector(2026, _dir).Normalize(new CaptureReader(_dir));

        Assert.Equal(["42", "43"], result.Elections.Select(e => e.ElectionId));
        Assert.Equal(["ANN ROE", "JANE Q. PUBLIC", "JOHN DOE"], result.Candidates.Select(c => c.CandidateName).Order());
        Assert.All(result.Candidates, c => Assert.Equal("https://candidates.wvsos.gov/candidate-web-api/candidates", c.SourceUrl));
    }

    [Fact]
    public void Normalize_OfACaptureWithNoCandidates_Throws()
    {
        var sink = new RawSink(_dir, "1", "WV", 2026);
        RecordPage(sink, 0);

        Assert.Throws<InvalidOperationException>(() => new WvCollector(2026, _dir).Normalize(new CaptureReader(_dir)));
    }

    private static void RecordPage(RawSink sink, int page, params string[] candidates) =>
        sink.Record(
            "POST", "https://candidates.wvsos.gov/candidate-web-api/candidates", $"request-{page}", 200, "application/json",
            Encoding.UTF8.GetBytes($$"""{ "data": { "candidates": [{{string.Join(",", candidates)}}] } }"""),
            5, null, FetchTag.Of(WvCandidateClient.PageRole, ("page", page.ToString())));

    private static string Candidate(int id, int electionId, string date, string type, string ballotName) => $$"""
        {
            "candidateId": {{id}}, "electionId": {{electionId}}, "officeId": 7,
            "electionName": "{{date}} {{type}}", "electionDate": "{{date}}", "electionType": "{{type}}",
            "officeName": "Governor", "candidateBallotName": "{{ballotName}}", "partyDescription": "Democrat"
        }
        """;
}
