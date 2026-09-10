using System.Text.Json;
using StateBallot.Core;
using StateBallot.Core.Publishing;

namespace StateBallot.Core.Tests;

public class ElectionTreeWriterTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "roster-tree-" + Guid.NewGuid().ToString("N"));
    private readonly string _stateDir;

    public ElectionTreeWriterTests()
    {
        _stateDir = Path.Combine(_root, "wv");
        Directory.CreateDirectory(_stateDir);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private static CollectResult SampleResult(params string[] dates)
    {
        var result = new CollectResult();
        foreach (var (date, i) in dates.Select((d, i) => (d, i)))
        {
            var day = DateOnly.Parse(date);
            result.Elections.Add(new Election
            {
                State = "WV", ElectionId = $"e{i}", Name = $"Election {date}", ElectionDate = day,
                ElectionType = i == 0 ? "PRIMARY" : "GENERAL", SourceUrl = "https://example.com",
            });
            result.Candidates.Add(new CandidateRow
            {
                State = "WV", ElectionDate = date, ElectionType = i == 0 ? "PRIMARY" : "GENERAL",
                Office = "GOVERNOR", CandidateName = $"Candidate {i}", SourceUrl = "https://example.com",
            });
            result.Measures.Add(new MeasureRow
            {
                State = "WV", ElectionDate = date, MeasureId = $"m{i}", Title = "Levy",
                Jurisdiction = "Kanawha County", County = "Kanawha", SourceUrl = "https://example.com",
            });
        }
        result.StatewideProposedMeasures.Add(new MeasureRow
        {
            State = "WV", ElectionDate = null, MeasureId = "Amendment 1", Title = "Proposed",
            Jurisdiction = "state", SourceUrl = "https://example.com",
        });
        result.CountyDirectory.Add(new CountyDirectoryRow
        {
            State = "WV", CountyName = "Kanawha", CountyFips = "54039",
            ElectionsOfficeUrl = "https://example.com", Address = "1 Main", Phone = "304",
        });
        result.Gaps.Add("nothing yet");
        result.Sources.Elections.Add(new SourceEntry("https://example.com/api", "json"));
        result.Sources.NextRun = new NextRunInfo { RecommendedAfter = "2026-10-01", Reason = "certification" };
        return result;
    }

    [Fact]
    public void WritesOneDirectoryPerElectionDate_PlusStateLevelFiles()
    {
        var report = new ElectionTreeWriter(_stateDir).Write(SampleResult("2026-05-12", "2026-11-03"), new RunContext(2026));

        string[] expectedDates = ["2026-05-12", "2026-11-03"];
        Assert.Equal(expectedDates, report.ElectionDates.ToArray());
        foreach (var date in report.ElectionDates)
        {
            var dir = Path.Combine(_stateDir, date);
            Assert.True(File.Exists(Path.Combine(dir, "elections.json")));
            Assert.True(File.Exists(Path.Combine(dir, "candidates.json")));
            Assert.True(File.Exists(Path.Combine(dir, "measures.json")));
            Assert.True(File.Exists(Path.Combine(dir, "run.json")));
            Assert.False(File.Exists(Path.Combine(dir, "county_ballots.json")));
        }
        Assert.True(File.Exists(Path.Combine(_stateDir, "county_directory.json")));
        Assert.True(File.Exists(Path.Combine(_stateDir, "proposed_measures.json")));
        Assert.False(File.Exists(Path.Combine(_stateDir, "candidates.json")));
    }

    [Fact]
    public void RowsAreSplitByDate_AndTypesNormalized()
    {
        new ElectionTreeWriter(_stateDir).Write(SampleResult("2026-05-12", "2026-11-03"), new RunContext(2026));

        using var may = JsonDocument.Parse(File.ReadAllText(Path.Combine(_stateDir, "2026-05-12", "candidates.json")));
        var row = Assert.Single(may.RootElement.EnumerateArray());
        Assert.Equal("Candidate 0", row.GetProperty("candidate_name").GetString());
        Assert.Equal("Primary", row.GetProperty("election_type").GetString());

        using var nov = JsonDocument.Parse(File.ReadAllText(Path.Combine(_stateDir, "2026-11-03", "elections.json")));
        Assert.Equal("General", Assert.Single(nov.RootElement.EnumerateArray()).GetProperty("election_type").GetString());
    }

    [Fact]
    public void RunJson_CarriesProvenance_AndValidates()
    {
        new ElectionTreeWriter(_stateDir).Write(
            SampleResult("2026-05-12"),
            new RunContext(2026) { Wayback = "20260101", CliArgs = "--state WV", RequestedBy = "tester" });

        var path = Path.Combine(_stateDir, "2026-05-12", "run.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var run = doc.RootElement;
        Assert.Equal("WV", run.GetProperty("state").GetString());
        Assert.Equal("2026-05-12", run.GetProperty("election_date").GetString());
        Assert.Equal(2026, run.GetProperty("year").GetInt32());
        Assert.Equal("20260101", run.GetProperty("wayback").GetString());
        Assert.Equal("tester", run.GetProperty("requested_by").GetString());
        string?[] expectedIds = ["e0"];
        Assert.Equal(expectedIds, run.GetProperty("election_ids").EnumerateArray().Select(e => e.GetString()).ToArray());
        Assert.False(run.GetProperty("pending").GetBoolean());
        Assert.Equal(1, run.GetProperty("counts").GetProperty("candidates").GetInt32());
        Assert.Equal("Primary", run.GetProperty("election_types_raw").GetProperty("PRIMARY").GetString());
        Assert.Equal("nothing yet", run.GetProperty("gaps")[0].GetString());
        Assert.Equal("2026-10-01", run.GetProperty("next_run").GetProperty("recommended_after").GetString());
        Assert.Equal("https://example.com/api", run.GetProperty("sources").GetProperty("elections")[0].GetProperty("url").GetString());
        Assert.False(run.GetProperty("sources").TryGetProperty("gaps", out _));

        Assert.Empty(new SchemaValidator().ValidateFile(path));
    }

    [Fact]
    public void PendingElection_IsFlagged()
    {
        var result = SampleResult("2026-11-03");
        result.Candidates.Clear();
        result.PendingElections.Add(result.Elections[0]);

        new ElectionTreeWriter(_stateDir).Write(result, new RunContext(2026));

        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(_stateDir, "2026-11-03", "run.json")));
        Assert.True(doc.RootElement.GetProperty("pending").GetBoolean());
        Assert.Equal("[]", File.ReadAllText(Path.Combine(_stateDir, "2026-11-03", "candidates.json")).Trim());
    }

    [Fact]
    public void RerunWithOtherDates_LeavesExistingDateDirectoriesAlone()
    {
        var writer = new ElectionTreeWriter(_stateDir);
        writer.Write(SampleResult("2024-05-14", "2024-11-05"), new RunContext(2024));
        var before = File.ReadAllText(Path.Combine(_stateDir, "2024-11-05", "candidates.json"));

        writer.Write(SampleResult("2026-05-12"), new RunContext(2026));

        Assert.True(Directory.Exists(Path.Combine(_stateDir, "2024-05-14")));
        Assert.Equal(before, File.ReadAllText(Path.Combine(_stateDir, "2024-11-05", "candidates.json")));
        Assert.True(Directory.Exists(Path.Combine(_stateDir, "2026-05-12")));
    }

    [Fact]
    public void RerunOfSameDate_ReplacesThatDirectoryCompletely()
    {
        var writer = new ElectionTreeWriter(_stateDir);
        writer.Write(SampleResult("2026-11-03"), new RunContext(2026));
        var stray = Path.Combine(_stateDir, "2026-11-03", "notes.txt");
        File.WriteAllText(stray, "leftover");

        writer.Write(SampleResult("2026-11-03"), new RunContext(2026));

        Assert.False(File.Exists(stray));
    }

    [Fact]
    public void LegacyFlatFiles_AreRemoved()
    {
        File.WriteAllText(Path.Combine(_stateDir, "candidates.json"), "[]");
        File.WriteAllText(Path.Combine(_stateDir, "candidates.csv"), "");

        new ElectionTreeWriter(_stateDir).Write(SampleResult("2026-11-03"), new RunContext(2026));

        Assert.False(File.Exists(Path.Combine(_stateDir, "candidates.json")));
        Assert.False(File.Exists(Path.Combine(_stateDir, "candidates.csv")));
    }

    [Fact]
    public void InvalidRow_FailsTheWrite()
    {
        var result = SampleResult("2026-11-03");
        result.Candidates[0].CandidateName = "";

        Assert.Throws<SchemaValidationException>(() =>
            new ElectionTreeWriter(_stateDir).Write(result, new RunContext(2026)));
    }
}
