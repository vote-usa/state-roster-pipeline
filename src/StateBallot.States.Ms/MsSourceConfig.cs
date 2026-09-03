namespace StateBallot.States.Ms;

/// <summary>
/// All source URLs and the headers this source needs in one place. The MS SOS
/// site (www.sos.ms.gov and this content host alike) sits behind an Akamai bot
/// rule that - unlike TX's Cloudflare case - blocks realistic *browser* User-
/// Agent strings (Chrome/Firefox/Safari, and any unrecognized custom string,
/// including HttpFetcher's own default "StateBallotRoster/1.0 (...)") while
/// letting plain HTTP-tool User-Agents (curl, python-requests, Wget) straight
/// through untouched - confirmed live via direct requests with each. The fix
/// here is the mirror image of TX's: instead of adding browser-like headers to
/// get past a "you don't look like a browser" check, this state overrides the
/// UA to a bare tool-like string to get past a "you look like an impersonated
/// browser" check.
/// </summary>
public sealed class MsSourceConfig
{
    /// <summary>
    /// The SOS's "Candidate Qualifying List" page - an ASP.NET WebForms page
    /// whose "Download CSV" button (see WebFormsPostback) returns the state's
    /// full current candidate roster in one response. Embedded via iframe in
    /// the public-facing www.sos.ms.gov/elections-voting/candidate-qualifying-list
    /// page; fetched directly here since the iframe target is the actual data
    /// source and the wrapper page adds nothing.
    /// </summary>
    public string CandidateQualifyingListUrl { get; init; } = "https://sos.ms.gov/content/CandidateQualifying/default.aspx";

    public const string DownloadCsvButtonName = "btnDownloadExcel";

    /// <summary>Headers HttpFetcher must carry for every request to this source.</summary>
    public static readonly IReadOnlyDictionary<string, string> ExtraHeaders = new Dictionary<string, string>
    {
        ["User-Agent"] = "curl/8.4.0",
    };
}
