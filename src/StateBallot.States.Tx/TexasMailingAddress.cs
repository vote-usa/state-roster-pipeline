using System.Text.Json.Serialization;

namespace StateBallot.States.Tx;

public sealed class TexasMailingAddress
{
    [JsonPropertyName("txStreetNumber")]
    public string? TxStreetNumber { get; set; }

    [JsonPropertyName("txStreetName")]
    public string? TxStreetName { get; set; }

    [JsonPropertyName("txStreetName2")]
    public string? TxStreetName2 { get; set; }

    [JsonPropertyName("txCity")]
    public string? TxCity { get; set; }

    [JsonPropertyName("cdState")]
    public string? CdState { get; set; }

    [JsonPropertyName("txZip5")]
    public string? TxZip5 { get; set; }

    [JsonPropertyName("cdAddressType")]
    public string? CdAddressType { get; set; }

    [JsonPropertyName("validAddress")]
    public bool ValidAddress { get; set; }
}
