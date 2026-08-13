using System.Text.Json;

namespace StateBallot.States.Wv.Tests;

public class WestVirginiaDtoTests
{
    [Fact]
    public void CandidateResponse_UnwrapsNestedDataCandidates()
    {
        var json = @"
        {
            ""data"": {
                ""candidates"": [
                    {
                        ""candidateId"": 101,
                        ""electionId"": 42,
                        ""officeId"": 7,
                        ""partyCode"": ""DEM"",
                        ""partyDescription"": ""Democrat"",
                        ""electionName"": ""2026 PRIMARY"",
                        ""electionDate"": ""2026-05-12"",
                        ""electionType"": ""PRIMARY"",
                        ""officeName"": ""Governor"",
                        ""candidateBallotName"": ""JANE Q. PUBLIC"",
                        ""candidateEmail"": ""jane@example.com"",
                        ""filingDate"": ""2026-01-10""
                    }
                ]
            }
        }";

        var response = JsonSerializer.Deserialize<WestVirginiaCandidateResponse>(json);

        Assert.NotNull(response?.Data?.Candidates);
        var candidate = Assert.Single(response.Data.Candidates);
        Assert.Equal(101, candidate.CandidateId);
        Assert.Equal("JANE Q. PUBLIC", candidate.CandidateBallotName);
        Assert.Equal("Democrat", candidate.PartyDescription);
        Assert.Equal("2026-05-12", candidate.ElectionDate);
    }

    [Fact]
    public void CandidateResponse_EmptyPage_YieldsEmptyList()
    {
        var json = @"{ ""data"": { ""candidates"": [] } }";

        var response = JsonSerializer.Deserialize<WestVirginiaCandidateResponse>(json);

        Assert.NotNull(response?.Data?.Candidates);
        Assert.Empty(response.Data.Candidates);
    }
}
