using StateBallot.Core;

namespace StateBallot.States.Ms;

/// <summary>
/// Named accessors over Mississippi's source links (data/input/ms/source_links.json, or the
/// SourceLinks table when a run has a database), plus the headers this source needs. The MS
/// SOS site sits behind an Akamai bot rule that - unlike TX's Cloudflare case - blocks
/// realistic *browser* User-Agent strings (and any unrecognized custom string, including
/// HttpFetcher's own default) while letting plain HTTP-tool User-Agents (curl,
/// python-requests, Wget) straight through - confirmed live via direct requests with each.
/// So this state overrides the UA to a bare tool-like string, the mirror image of TX.
/// </summary>
public sealed class MsSourceConfig : SourceConfigBase
{
    /// <summary>
    /// The SOS's "Candidate Qualifying List" page - an ASP.NET WebForms page whose
    /// "Download CSV" button (see WebFormsPostback) returns the state's full current
    /// candidate roster in one response.
    /// </summary>
    public string CandidateQualifyingListUrl => Links.Url("qualifying-list-page");

    public const string DownloadCsvButtonName = "btnDownloadExcel";

    /// <summary>Headers HttpFetcher must carry for every request to this source.</summary>
    public static readonly IReadOnlyDictionary<string, string> ExtraHeaders = new Dictionary<string, string>
    {
        ["User-Agent"] = "curl/8.4.0",
    };
}
