using System.Text;
using System.Text.Json;
using StateBallot.Core;
using StateBallot.Core.Raw;

namespace StateBallot.States.Tx.Tests;

public sealed class TxNormalizeTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "tx-capture-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void Normalize_FiltersUpcomingElectionsAsOfTheCaptureDate()
    {
        var config = new TxSourceConfig();
        var sink = new RawSink(_dir, "1", "TX", 2026);
        Record(sink, config.ElectionsUrl(2026), FetchTag.Of(TxElectionClient.Role), """
            [
                { "idElection": 1, "txElectionName": "2026 MARCH PRIMARY", "cdElectionType": "P", "dtElectionDate": "2026-03-03" },
                { "idElection": 2, "txElectionName": "2026 NOVEMBER GENERAL", "cdElectionType": "G", "dtElectionDate": "2026-11-03" }
            ]
            """);
        Record(sink, config.CandidatesUrl, FetchTag.Of(TxCandidateClient.Role, ("election", "2")), """
            [ { "idCandidate": 9, "idElection": 2, "cdParty": "R", "txOfficeName": "GOVERNOR", "txFullNameBallot": "PAT SMITH" } ]
            """);
        SetCaptureDate(new DateTime(2026, 9, 23, 12, 0, 0, DateTimeKind.Utc));

        var result = new TxCollector(2026, _dir).Normalize(new CaptureReader(_dir));

        var election = Assert.Single(result.Elections);
        Assert.Equal("2", election.ElectionId);
        var candidate = Assert.Single(result.Candidates);
        Assert.Equal("PAT SMITH", candidate.CandidateName);
        Assert.Equal("2", candidate.SourceElectionId);
    }

    [Fact]
    public void Normalize_WhenTheCaptureLacksAnElectionsCandidates_Throws()
    {
        var sink = new RawSink(_dir, "1", "TX", 2026);
        Record(sink, new TxSourceConfig().ElectionsUrl(2026), FetchTag.Of(TxElectionClient.Role), """
            [ { "idElection": 2, "txElectionName": "2026 NOVEMBER GENERAL", "cdElectionType": "G", "dtElectionDate": "2026-11-03" } ]
            """);
        SetCaptureDate(new DateTime(2026, 9, 23, 12, 0, 0, DateTimeKind.Utc));

        var ex = Assert.Throws<InvalidOperationException>(() => new TxCollector(2026, _dir).Normalize(new CaptureReader(_dir)));
        Assert.Contains("'candidates' (election=2)", ex.Message);
    }

    private static void Record(RawSink sink, string url, FetchTag tag, string json) =>
        sink.Record("GET", url, null, 200, "application/json", Encoding.UTF8.GetBytes(json), 5, null, tag);

    private void SetCaptureDate(DateTime startedAt)
    {
        var log = RawSink.ReadLog(_dir);
        log.StartedAt = startedAt;
        File.WriteAllText(Path.Combine(_dir, RawSink.LogFileName), JsonSerializer.Serialize(log, OutputWriter.JsonOptions));
    }
}
