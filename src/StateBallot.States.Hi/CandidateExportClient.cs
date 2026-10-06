using AngleSharp.Html.Parser;
using StateBallot.Core;

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
    private readonly HttpFetcher _fetcher;
    private readonly HiSourceConfig _config;

    public CandidateExportClient(HttpFetcher fetcher, HiSourceConfig config)
    {
        _fetcher = fetcher;
        _config = config;
    }

    public async Task<(List<Dictionary<string, string>> Rows, string PageUrl)> FetchAsync(int year)
    {
        var electionId = await FindElectionIdAsync(year);
        var pageUrl = _config.CandidateFilingPageUrl(electionId);
        var html = await _fetcher.GetStringAsync(pageUrl);

        var csvText = await WebFormsPostback.ClickButtonAsync(_fetcher, pageUrl, html, HiSourceConfig.ExportToCsvButtonName);
        var rows = DelimitedTableParser.Parse(csvText);
        ScrapeGuard.RequireAny(rows, () => $"No rows parsed from the CSV export at {pageUrl}.");

        return (rows, pageUrl);
    }

    private async Task<string> FindElectionIdAsync(int year)
    {
        var bootstrapUrl = _config.CandidateFilingPageUrl(_config.BootstrapElectionId);
        var html = await _fetcher.GetStringAsync(bootstrapUrl);
        var doc = await new HtmlParser().ParseDocumentAsync(html);

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
