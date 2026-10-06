using AngleSharp.Html.Parser;
using StateBallot.Core;
using StateBallot.Core.Raw;

namespace StateBallot.States.Hi;

/// <summary>
/// Fetches HI's full candidate roster via the CandidateFiling.aspx page's
/// "Export to CSV" button. That page's own Telerik grid is paginated (~24 rows
/// per page), but the export button - confirmed a real
/// &lt;input type="submit"&gt;, not a client-side-only postback - returns the
/// complete, un-paginated dataset in one response (verified live: 415 rows,
/// versus ~24 visible on the page itself). Two steps: find the target year's
/// opaque "elid" via the page's own election dropdown (HI's ids don't follow a
/// formula from the year the way every other state's URLs do), then replay
/// that specific page's postback to click the export button.
/// </summary>
public sealed class CandidateExportClient
{
    public const string BootstrapPageRole = "candidate-filing-bootstrap";
    public const string FilingPageRole = "candidate-filing-page";
    public const string ExportRole = "candidate-export";

    private readonly HiSourceConfig _config;

    public CandidateExportClient(HiSourceConfig config) => _config = config;

    public async Task CaptureAsync(HttpFetcher fetcher, int year)
    {
        var bootstrapUrl = _config.CandidateFilingPageUrl(_config.BootstrapElectionId);
        var electionId = FindElectionId(await fetcher.GetStringAsync(bootstrapUrl, FetchTag.Of(BootstrapPageRole)), year);
        var pageUrl = _config.CandidateFilingPageUrl(electionId);
        var html = await fetcher.GetStringAsync(pageUrl, FetchTag.Of(FilingPageRole));
        await WebFormsPostback.ClickButtonAsync(
            fetcher, pageUrl, html, HiSourceConfig.ExportToCsvButtonName, tag: FetchTag.Of(ExportRole));
    }

    public (List<Dictionary<string, string>> Rows, string PageUrl) Parse(CaptureReader capture, int year)
    {
        var electionId = FindElectionId(capture.Require(BootstrapPageRole).Text(), year);
        var pageUrl = _config.CandidateFilingPageUrl(electionId);

        var rows = DelimitedTableParser.Parse(capture.Require(ExportRole).Text());
        ScrapeGuard.RequireAny(rows, () => $"No rows parsed from the CSV export at {pageUrl}.");

        return (rows, pageUrl);
    }

    private string FindElectionId(string html, int year)
    {
        var bootstrapUrl = _config.CandidateFilingPageUrl(_config.BootstrapElectionId);
        var doc = new HtmlParser().ParseDocument(html);

        var options = doc.QuerySelector(HiSelectors.ElectionDropdownSelector)?.QuerySelectorAll("option")
            ?? Enumerable.Empty<AngleSharp.Dom.IElement>();
        var targetLabel = HiSelectors.ElectionReportLabel(year);
        var match = options.FirstOrDefault(o => string.Equals(o.TextContent.Trim(), targetLabel, StringComparison.OrdinalIgnoreCase));

        if (match is null)
        {
            var available = string.Join(", ", options.Select(o => o.TextContent.Trim()));
            throw new InvalidOperationException(
                $"No '{targetLabel}' option found in the election dropdown at {bootstrapUrl}. Available: {available}.");
        }

        return match.GetAttribute("value")
               ?? throw new InvalidOperationException($"'{targetLabel}' option has no value at {bootstrapUrl}.");
    }
}
