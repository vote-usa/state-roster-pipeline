using StateBallot.Core;

namespace StateBallot.States.Tx;

/// <summary>
/// Named accessors over Texas's source links (data/input/tx/source_links.json, or the
/// SourceLinks table when a run has a database), plus the browser-like headers this source
/// needs. The CivixApps API sits behind Cloudflare and rejects requests that don't look
/// like they came from a browser tab on goelect.txelections.civixapps.com.
/// </summary>
public sealed class TxSourceConfig : SourceConfigBase
{
    public string ElectionsUrl(int year) => Links.Url("elections", year: year);
    public string CandidatesUrl => Links.Url("candidates");

    /// <summary>Headers HttpFetcher must carry for every request to this source.</summary>
    public static readonly IReadOnlyDictionary<string, string> ExtraHeaders = new Dictionary<string, string>
    {
        ["User-Agent"] = "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36",
        ["Origin"] = "https://goelect.txelections.civixapps.com",
        ["Referer"] = "https://goelect.txelections.civixapps.com/",
    };
}
