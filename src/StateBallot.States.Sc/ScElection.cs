using System.Text.Json.Serialization;

namespace StateBallot.States.Sc;

/// <summary>Raw DTO for a single election as returned by GetElections.</summary>
public sealed class ScElection
{
    [JsonPropertyName("electionId")]
    public string ElectionId { get; set; } = "";

    [JsonPropertyName("electionName")]
    public string ElectionName { get; set; } = "";

    [JsonPropertyName("displayName")]
    public string DisplayName { get; set; } = "";

    [JsonPropertyName("electionDate")]
    public DateTime ElectionDate { get; set; }
}
