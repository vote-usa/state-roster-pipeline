using System.Text.Json.Serialization;

namespace StateBallot.States.Wv;

public sealed class WestVirginiaCandidateResponse
{
    [JsonPropertyName("data")]
    public WestVirginiaCandidateData? Data { get; set; }
}

public sealed class WestVirginiaCandidateData
{
    [JsonPropertyName("candidates")]
    public List<WestVirginiaCandidate>? Candidates { get; set; }
}
