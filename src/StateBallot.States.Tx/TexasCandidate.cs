using System.Text.Json.Serialization;

namespace StateBallot.States.Tx;

/// <summary>Raw DTO for a single candidate as returned by the CivixApps findQualifiedCandidates API.</summary>
public sealed class TexasCandidate
{
    [JsonPropertyName("idCandidate")]
    public int IdCandidate { get; set; }

    [JsonPropertyName("cdStatus")]
    public string? CdStatus { get; set; }

    [JsonPropertyName("cdCandType")]
    public string? CdCandType { get; set; }

    [JsonPropertyName("cdParty")]
    public string? CdParty { get; set; }

    [JsonPropertyName("idElection")]
    public int IdElection { get; set; }

    [JsonPropertyName("idOffice")]
    public int IdOffice { get; set; }

    [JsonPropertyName("cdFilingStatus")]
    public string? CdFilingStatus { get; set; }

    [JsonPropertyName("nbSortOrder")]
    public int NbSortOrder { get; set; }

    [JsonPropertyName("nbSecondarySortOrder")]
    public int NbSecondarySortOrder { get; set; }

    [JsonPropertyName("txLastNameBallot")]
    public string? TxLastNameBallot { get; set; }

    [JsonPropertyName("txFirstNameBallot")]
    public string? TxFirstNameBallot { get; set; }

    [JsonPropertyName("txFullNameBallot")]
    public string? TxFullNameBallot { get; set; }

    [JsonPropertyName("dtFiled")]
    public string? DtFiled { get; set; }

    [JsonPropertyName("txEmail")]
    public string? TxEmail { get; set; }

    [JsonPropertyName("txOccupation")]
    public string? TxOccupation { get; set; }

    [JsonPropertyName("flActive")]
    public bool FlActive { get; set; }

    [JsonPropertyName("mailingAddress")]
    public TexasMailingAddress? MailingAddress { get; set; }

    [JsonPropertyName("txElectionName")]
    public string? TxElectionName { get; set; }

    [JsonPropertyName("txOfficeName")]
    public string? TxOfficeName { get; set; }

    [JsonPropertyName("cdOfficeType")]
    public string? CdOfficeType { get; set; }

    [JsonPropertyName("txOfficeTypeName")]
    public string? TxOfficeTypeName { get; set; }
}
