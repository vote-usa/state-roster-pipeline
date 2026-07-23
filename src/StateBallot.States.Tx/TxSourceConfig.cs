namespace StateBallot.States.Tx;

/// <summary>
/// All source URLs and the browser-like headers this source needs in one place.
/// The CivixApps API sits behind Cloudflare and rejects requests that don't look
/// like they came from a browser tab on goelect.txelections.civixapps.com.
/// </summary>
public sealed class TxSourceConfig
{
    public string BaseUrl { get; init; } = "https://goelect.txelections.civixapps.com";
    public string CandidatesEndpoint { get; init; } = "/api-ivis-cbp/api/cbp/findQualifiedCandidates";
    public string ElectionsEndpoint { get; init; } = "/api-ivis-cbp/api/cbp/getElectionsByYear";

    public string ElectionsUrl(int year) => $"{BaseUrl}{ElectionsEndpoint}/{year}";
    public string CandidatesUrl => $"{BaseUrl}{CandidatesEndpoint}";

    /// <summary>Headers HttpFetcher must carry for every request to this source.</summary>
    public static readonly IReadOnlyDictionary<string, string> ExtraHeaders = new Dictionary<string, string>
    {
        ["User-Agent"] = "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36",
        ["Origin"] = "https://goelect.txelections.civixapps.com",
        ["Referer"] = "https://goelect.txelections.civixapps.com/",
    };
}
