using System.Text.Json.Serialization;

namespace StateBallot.States.Wv;

/// <summary>Raw DTO for a single candidate as returned by the WV SOS candidate-web-api.</summary>
public sealed class WestVirginiaCandidate
{
    [JsonPropertyName("candidateId")]
    public int CandidateId { get; set; }

    [JsonPropertyName("sosCandidateId")]
    public string? SosCandidateId { get; set; }

    [JsonPropertyName("candidateType")]
    public string? CandidateType { get; set; }

    [JsonPropertyName("electionId")]
    public int ElectionId { get; set; }

    [JsonPropertyName("officeId")]
    public int OfficeId { get; set; }

    [JsonPropertyName("partyCode")]
    public string? PartyCode { get; set; }

    [JsonPropertyName("partyDescription")]
    public string? PartyDescription { get; set; }

    [JsonPropertyName("electionName")]
    public string? ElectionName { get; set; }

    [JsonPropertyName("electionDate")]
    public string? ElectionDate { get; set; }

    [JsonPropertyName("officeName")]
    public string? OfficeName { get; set; }

    [JsonPropertyName("candidateFirstName")]
    public string? CandidateFirstName { get; set; }

    [JsonPropertyName("candidateLastName")]
    public string? CandidateLastName { get; set; }

    [JsonPropertyName("candidateMiddleName")]
    public string? CandidateMiddleName { get; set; }

    [JsonPropertyName("candidateSuffixName")]
    public string? CandidateSuffixName { get; set; }

    [JsonPropertyName("candidateBallotName")]
    public string? CandidateBallotName { get; set; }

    [JsonPropertyName("candidateEmail")]
    public string? CandidateEmail { get; set; }

    [JsonPropertyName("candidatePhoneNumber")]
    public string? CandidatePhoneNumber { get; set; }

    [JsonPropertyName("campaignPhoneNumber")]
    public string? CampaignPhoneNumber { get; set; }

    [JsonPropertyName("divisionNumber")]
    public string? DivisionNumber { get; set; }

    [JsonPropertyName("magisterialDistrict")]
    public string? MagisterialDistrict { get; set; }

    [JsonPropertyName("website")]
    public string? Website { get; set; }

    [JsonPropertyName("committeeName")]
    public string? CommitteeName { get; set; }

    [JsonPropertyName("electionType")]
    public string? ElectionType { get; set; }

    [JsonPropertyName("electionCategory")]
    public string? ElectionCategory { get; set; }

    [JsonPropertyName("filingDate")]
    public string? FilingDate { get; set; }

    [JsonPropertyName("officeDescription")]
    public string? OfficeDescription { get; set; }

    [JsonPropertyName("townName")]
    public string? TownName { get; set; }

    [JsonPropertyName("candidateDistrictName")]
    public string? CandidateDistrictName { get; set; }

    [JsonPropertyName("residentialAddress")]
    public WestVirginiaResidentialAddress? ResidentialAddress { get; set; }

    [JsonPropertyName("mailingAddress")]
    public WestVirginiaAddress? MailingAddress { get; set; }
}
