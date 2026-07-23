using System.Text.Json.Serialization;

namespace StateBallot.States.Wv;

public sealed class WestVirginiaResidentialAddress
{
    [JsonPropertyName("city")]
    public string? City { get; set; }

    [JsonPropertyName("state")]
    public string? State { get; set; }

    [JsonPropertyName("countyDescription")]
    public string? CountyDescription { get; set; }
}
