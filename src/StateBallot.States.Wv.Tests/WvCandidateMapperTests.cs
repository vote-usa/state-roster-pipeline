using StateBallot.Core;

namespace StateBallot.States.Wv.Tests;

public class WvCandidateMapperTests
{
    private static readonly string[] DateFormats = { "yyyy-MM-dd", "yyyy-MM-ddTHH:mm:ss", "M/d/yyyy", "MM/dd/yyyy" };

    [Fact]
    public void ToElection_ParsesIsoDate()
    {
        var raw = new WestVirginiaCandidate
        {
            ElectionId = 42,
            ElectionName = "2026 PRIMARY",
            ElectionDate = "2026-05-12",
            ElectionType = "PRIMARY",
        };

        var election = WvCandidateMapper.ToElection(raw, DateFormats);

        Assert.Equal("WV", election.State);
        Assert.Equal("42", election.ElectionId);
        Assert.Equal(new DateOnly(2026, 5, 12), election.ElectionDate);
    }

    [Fact]
    public void ToElection_UnrecognizedDateFormat_ThrowsWithElectionId()
    {
        var raw = new WestVirginiaCandidate { ElectionId = 99, ElectionDate = "not-a-date" };

        var ex = Assert.Throws<InvalidOperationException>(() => WvCandidateMapper.ToElection(raw, DateFormats));
        Assert.Contains("99", ex.Message);
    }

    [Fact]
    public void ToCandidateRow_UsesBallotNameWhenPresent()
    {
        var election = WvCandidateMapper.ToElection(new WestVirginiaCandidate { ElectionId = 1, ElectionDate = "2026-05-12" }, DateFormats);
        var candidate = new WestVirginiaCandidate
        {
            CandidateId = 5,
            OfficeId = 77,
            ElectionCategory = "STATEWIDE",
            OfficeDescription = "STATE RACE",
            CandidateBallotName = "JANE Q. PUBLIC",
            CandidateFirstName = "Jane",
            CandidateMiddleName = "Q.",
            CandidateLastName = "Public",
            CandidateSuffixName = "Jr.",
            PartyDescription = "Democrat",
            OfficeName = "Governor",
            CandidateEmail = "jane@example.com",
            CandidatePhoneNumber = "304-555-0100",
        };

        var data = WvCandidateMapper.ToCandidateRow(candidate, election, "https://example.com");

        Assert.Equal("JANE Q. PUBLIC", data.CandidateName);
        Assert.Equal("Democrat", data.Party);
        Assert.Equal("5", data.SourceCandidateId);
        Assert.Equal("jane@example.com", data.Email);
        Assert.Equal("304-555-0100", data.Phone);
        Assert.Equal("77", data.SourceOfficeId);
        Assert.Equal("STATE RACE", data.SourceOfficeType);
        Assert.Equal("Jane", data.FirstName);
        Assert.Equal("Q.", data.MiddleName);
        Assert.Equal("Public", data.LastName);
        Assert.Equal("Jr.", data.Suffix);
        Assert.Null(data.LocalJurisdiction);
    }

    [Fact]
    public void ToCandidateRow_MunicipalElection_AppendsCategory_AndIgnoresTownName()
    {
        var election = WvCandidateMapper.ToElection(new WestVirginiaCandidate { ElectionId = 1, ElectionDate = "2026-06-09" }, DateFormats);
        var candidate = new WestVirginiaCandidate
        {
            CandidateId = 6,
            OfficeId = 0,
            ElectionCategory = "MUNICIPAL",
            OfficeDescription = "COUNTY",
            TownName = "LINCOLN", // county name in practice, not a municipality
            CandidateBallotName = "JOHN DOE",
        };

        var data = WvCandidateMapper.ToCandidateRow(candidate, election, "https://example.com");

        Assert.Null(data.SourceOfficeId);
        Assert.Equal("COUNTY/MUNICIPAL", data.SourceOfficeType);
        Assert.Null(data.LocalJurisdiction);
        Assert.Null(data.Suffix);
    }

    [Theory]
    [InlineData("FEDERAL", "STATEWIDE", "FEDERAL")]
    [InlineData("COUNTY RACE", "STATEWIDE", "COUNTY RACE")]
    [InlineData(null, "MUNICIPAL", "MUNICIPAL")]
    [InlineData(" ", " ", null)]
    public void SourceOfficeType_PrefersOfficeDescription(string? description, string? category, string? expected)
    {
        var raw = new WestVirginiaCandidate { OfficeDescription = description, ElectionCategory = category };

        Assert.Equal(expected, WvCandidateMapper.SourceOfficeType(raw));
    }

    [Fact]
    public void ToCandidateRow_FormatsMailingAddress()
    {
        var election = WvCandidateMapper.ToElection(new WestVirginiaCandidate { ElectionId = 1, ElectionDate = "2026-05-12" }, DateFormats);
        var candidate = new WestVirginiaCandidate
        {
            CandidateBallotName = "Jane Public",
            MailingAddress = new WestVirginiaAddress
            {
                StreetNumber = "123",
                Street1 = "Main St",
                City = "Charleston",
                State = "WV",
                Zip5 = "25301",
            },
        };

        var data = WvCandidateMapper.ToCandidateRow(candidate, election, "https://example.com");

        Assert.Equal("123 Main St", data.MailingAddressLine);
        Assert.Equal("Charleston", data.MailingCity);
        Assert.Equal("WV", data.MailingState);
        Assert.Equal("25301", data.MailingZip);
    }

    [Fact]
    public void ToCandidateRow_NoMailingAddress_LeavesAddressFieldsNull()
    {
        var election = WvCandidateMapper.ToElection(new WestVirginiaCandidate { ElectionId = 1, ElectionDate = "2026-05-12" }, DateFormats);
        var candidate = new WestVirginiaCandidate { CandidateBallotName = "Jane Public" };

        var data = WvCandidateMapper.ToCandidateRow(candidate, election, "https://example.com");

        Assert.Null(data.MailingAddressLine);
        Assert.Null(data.MailingCity);
    }

    [Fact]
    public void DeduplicationKey_DistinguishesByFilingDate()
    {
        var a = new WestVirginiaCandidate { CandidateId = 1, CandidateBallotName = "X", ElectionId = 1, OfficeId = 1, FilingDate = "2026-01-01" };
        var b = new WestVirginiaCandidate { CandidateId = 1, CandidateBallotName = "X", ElectionId = 1, OfficeId = 1, FilingDate = "2026-01-02" };

        var deduped = Deduplicator.RemoveDuplicates(new[] { a, b }, WvCandidateMapper.DeduplicationKey);

        Assert.Equal(2, deduped.Count);
    }

    [Fact]
    public void DeduplicationKey_RemovesExactDuplicates()
    {
        var a = new WestVirginiaCandidate { CandidateId = 1, CandidateBallotName = "X", ElectionId = 1, OfficeId = 1, FilingDate = "2026-01-01" };
        var b = new WestVirginiaCandidate { CandidateId = 1, CandidateBallotName = "X", ElectionId = 1, OfficeId = 1, FilingDate = "2026-01-01" };

        var deduped = Deduplicator.RemoveDuplicates(new[] { a, b }, WvCandidateMapper.DeduplicationKey);

        Assert.Single(deduped);
    }
}
