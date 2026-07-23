using System.Text.Json.Serialization;

namespace StateBallot.States.Tx;

/// <summary>Raw DTO for a single election as returned by the CivixApps getElectionsByYear API.</summary>
public sealed class TexasElection
{
    [JsonPropertyName("idElection")]
    public int IdElection { get; set; }

    [JsonPropertyName("txElectionName")]
    public string? TxElectionName { get; set; }

    [JsonPropertyName("cdElectionType")]
    public string? CdElectionType { get; set; }

    [JsonPropertyName("cdElectionCategory")]
    public string? CdElectionCategory { get; set; }

    [JsonPropertyName("dtElectionDate")]
    public string? DtElectionDate { get; set; }

    [JsonPropertyName("dtRegistrationDeadlineDate")]
    public string? DtRegistrationDeadlineDate { get; set; }

    [JsonPropertyName("dtElectionCertificationDate")]
    public string? DtElectionCertificationDate { get; set; }

    [JsonPropertyName("flOpenToCounty")]
    public bool FlOpenToCounty { get; set; }

    [JsonPropertyName("flFacebookPushNotification")]
    public bool FlFacebookPushNotification { get; set; }

    [JsonPropertyName("flTwitterPushNotification")]
    public bool FlTwitterPushNotification { get; set; }

    [JsonPropertyName("flLinkedInPushNotification")]
    public bool FlLinkedInPushNotification { get; set; }

    [JsonPropertyName("flIncludeOnlineVoterRegistrationLink")]
    public bool FlIncludeOnlineVoterRegistrationLink { get; set; }

    [JsonPropertyName("flCustomNotification")]
    public bool FlCustomNotification { get; set; }

    [JsonPropertyName("txComment")]
    public string? TxComment { get; set; }
}
