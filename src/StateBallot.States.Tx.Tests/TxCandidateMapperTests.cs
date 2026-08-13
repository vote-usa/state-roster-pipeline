namespace StateBallot.States.Tx.Tests;

public class TxCandidateMapperTests
{
    [Fact]
    public void ToElection_ParsesDateAndNormalizesType()
    {
        var raw = new TexasElection
        {
            IdElection = 53814,
            TxElectionName = "2026 DEMOCRATIC PRIMARY ELECTION",
            CdElectionType = "P",
            DtElectionDate = "2026-03-03",
        };

        var election = TxCandidateMapper.ToElection(raw);

        Assert.Equal("TX", election.State);
        Assert.Equal("53814", election.ElectionId);
        Assert.Equal(new DateOnly(2026, 3, 3), election.ElectionDate);
        Assert.Equal("Primary", election.ElectionType);
    }

    [Fact]
    public void ToElection_UnknownTypeCode_PassesThroughRaw()
    {
        var raw = new TexasElection { IdElection = 1, DtElectionDate = "2026-11-03", CdElectionType = "X" };

        var election = TxCandidateMapper.ToElection(raw);

        Assert.Equal("X", election.ElectionType);
    }

    [Fact]
    public void ToCandidateRow_MapsFieldsAndFormatsMailingAddress()
    {
        var election = TxCandidateMapper.ToElection(new TexasElection
        {
            IdElection = 56181,
            DtElectionDate = "2026-04-15",
            CdElectionType = "S",
        });

        var candidate = new TexasCandidate
        {
            IdCandidate = 36388,
            CdParty = "R",
            TxFullNameBallot = "BRETT W. LIGON",
            TxOfficeName = "STATE SENATOR, DISTRICT 4",
            TxOccupation = "ATTORNEY",
            DtFiled = "2026-01-22",
            TxEmail = "brett@example.com",
            MailingAddress = new TexasMailingAddress
            {
                TxStreetNumber = "1",
                TxStreetName = "E GREENWAY PLZ",
                TxStreetName2 = "STE 225",
                TxCity = "HOUSTON",
                CdState = "TX",
                TxZip5 = "77046",
            },
        };

        var data = TxCandidateMapper.ToCandidateRow(candidate, election, "https://example.com/api");

        Assert.Equal("TX", data.State);
        Assert.Equal("2026-04-15", data.ElectionDate);
        Assert.Equal("Special", data.ElectionType);
        Assert.Equal("STATE SENATOR, DISTRICT 4", data.Office);
        Assert.Equal("BRETT W. LIGON", data.CandidateName);
        Assert.Equal("R", data.Party);
        Assert.Equal("36388", data.SourceCandidateId);
        Assert.Equal("2026-01-22", data.FilingDate);
        Assert.Equal("brett@example.com", data.Email);
        Assert.Equal("ATTORNEY", data.Occupation);
        Assert.Equal("1 E GREENWAY PLZ STE 225", data.MailingAddressLine);
        Assert.Equal("HOUSTON", data.MailingCity);
        Assert.Equal("TX", data.MailingState);
        Assert.Equal("77046", data.MailingZip);
        Assert.Null(data.Incumbent);
        Assert.Null(data.District);
        Assert.Null(data.County);
    }

    [Fact]
    public void ToCandidateRow_NoMailingAddress_LeavesAddressFieldsNull()
    {
        var election = TxCandidateMapper.ToElection(new TexasElection { IdElection = 1, DtElectionDate = "2026-11-03" });
        var candidate = new TexasCandidate { IdCandidate = 1, TxFullNameBallot = "JANE DOE" };

        var data = TxCandidateMapper.ToCandidateRow(candidate, election, "https://example.com");

        Assert.Null(data.MailingAddressLine);
        Assert.Null(data.MailingCity);
    }
}
