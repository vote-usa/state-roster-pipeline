using System.Text.Json.Serialization;

namespace StateBallot.States.Wv;

public sealed class WestVirginiaCandidateSearchRequest
{
    [JsonPropertyName("electionYear")]
    public int ElectionYear { get; set; }

    [JsonPropertyName("electionType")]
    public string? ElectionType { get; set; }

    [JsonPropertyName("page")]
    public int Page { get; set; }

    [JsonPropertyName("size")]
    public int Size { get; set; } = 1000;
}
