using System.Text.Json.Serialization;

namespace StateBallot.States.Tx;

public sealed class TxCandidateSearchRequest
{
    [JsonPropertyName("electionYear")]
    public int ElectionYear { get; set; }

    [JsonPropertyName("electionId")]
    public int ElectionId { get; set; }

    [JsonPropertyName("party")]
    public string? Party { get; set; }

    [JsonPropertyName("officeId")]
    public int? OfficeId { get; set; }

    [JsonPropertyName("officeType")]
    public string? OfficeType { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("countyId")]
    public int? CountyId { get; set; }
}
