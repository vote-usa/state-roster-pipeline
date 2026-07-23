using System.Text.Json.Serialization;

namespace StateBallot.States.Wv;

public sealed class WestVirginiaAddress
{
    [JsonPropertyName("streetNumber")]
    public string? StreetNumber { get; set; }

    [JsonPropertyName("street1")]
    public string? Street1 { get; set; }

    [JsonPropertyName("street2")]
    public string? Street2 { get; set; }

    [JsonPropertyName("city")]
    public string? City { get; set; }

    [JsonPropertyName("state")]
    public string? State { get; set; }

    [JsonPropertyName("zip5")]
    public string? Zip5 { get; set; }

    [JsonPropertyName("zip4")]
    public string? Zip4 { get; set; }

    [JsonPropertyName("county")]
    public string? County { get; set; }

    [JsonPropertyName("countyDescription")]
    public string? CountyDescription { get; set; }
}
