namespace StateBallot.States.Wv;

/// <summary>All source URLs for the WV SOS candidate API in one place.</summary>
public sealed class WvSourceConfig
{
    public string BaseUrl { get; init; } = "https://candidates.wvsos.gov";
    public string Endpoint { get; init; } = "/candidate-web-api/candidates";

    public string CandidatesUrl => $"{BaseUrl}{Endpoint}";
}
