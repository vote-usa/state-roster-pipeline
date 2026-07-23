using System.Text.Json;

namespace StateBallot.States.Tx.Tests;

public class TexasDtoTests
{
    [Fact]
    public void TexasCandidate_DeserializesCorrectly()
    {
        var json = @"
        {
            ""idCandidate"": 36388,
            ""cdStatus"": ""A"",
            ""cdParty"": ""R"",
            ""idElection"": 56181,
            ""idOffice"": 5031,
            ""cdFilingStatus"": ""SP"",
            ""nbSortOrder"": 50,
            ""nbSecondarySortOrder"": 4,
            ""txLastNameBallot"": ""LIGON"",
            ""txFirstNameBallot"": ""BRETT W."",
            ""txFullNameBallot"": ""BRETT W. LIGON"",
            ""dtFiled"": ""2026-01-22"",
            ""txOccupation"": ""ATTORNEY"",
            ""flActive"": true,
            ""mailingAddress"": {
                ""txStreetNumber"": ""1"",
                ""txStreetName"": ""E GREENWAY PLZ"",
                ""txStreetName2"": ""STE 225"",
                ""txCity"": ""HOUSTON"",
                ""cdState"": ""TX"",
                ""txZip5"": ""77046"",
                ""cdAddressType"": ""M"",
                ""validAddress"": true
            },
            ""txElectionName"": ""2026 SPECIAL ELECTION SENATE DISTRICT 4"",
            ""txOfficeName"": ""STATE SENATOR, DISTRICT 4 - UNEXPIRED TERM"",
            ""cdOfficeType"": ""SR"",
            ""txOfficeTypeName"": ""State""
        }";

        var candidate = JsonSerializer.Deserialize<TexasCandidate>(json);

        Assert.NotNull(candidate);
        Assert.Equal(36388, candidate.IdCandidate);
        Assert.Equal("R", candidate.CdParty);
        Assert.Equal("BRETT W. LIGON", candidate.TxFullNameBallot);
        Assert.Equal("ATTORNEY", candidate.TxOccupation);
        Assert.True(candidate.FlActive);

        Assert.NotNull(candidate.MailingAddress);
        Assert.Equal("HOUSTON", candidate.MailingAddress.TxCity);
        Assert.Equal("TX", candidate.MailingAddress.CdState);
        Assert.Equal("77046", candidate.MailingAddress.TxZip5);
    }

    [Fact]
    public void TxCandidateSearchRequest_SerializesCorrectly()
    {
        var request = new TxCandidateSearchRequest { ElectionYear = 2026, ElectionId = 56181 };

        var json = JsonSerializer.Serialize(request);
        var deserialized = JsonSerializer.Deserialize<TxCandidateSearchRequest>(json);

        Assert.NotNull(deserialized);
        Assert.Equal(2026, deserialized.ElectionYear);
        Assert.Equal(56181, deserialized.ElectionId);
        Assert.Null(deserialized.Party);
    }

    [Fact]
    public void TexasElection_DeserializesCorrectly()
    {
        var json = @"
        {
            ""idElection"": 53814,
            ""txElectionName"": ""2026 DEMOCRATIC PRIMARY ELECTION"",
            ""cdElectionType"": ""P"",
            ""cdElectionCategory"": ""SW"",
            ""dtElectionDate"": ""2026-03-03"",
            ""flOpenToCounty"": true
        }";

        var election = JsonSerializer.Deserialize<TexasElection>(json);

        Assert.NotNull(election);
        Assert.Equal(53814, election.IdElection);
        Assert.Equal("2026 DEMOCRATIC PRIMARY ELECTION", election.TxElectionName);
        Assert.Equal("P", election.CdElectionType);
        Assert.Equal("2026-03-03", election.DtElectionDate);
        Assert.True(election.FlOpenToCounty);
    }
}
