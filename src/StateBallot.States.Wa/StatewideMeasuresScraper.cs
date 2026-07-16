using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using StateBallot.Core;

namespace StateBallot.States.Wa;

/// <summary>
/// Parses the SoS "Proposed Ballot Measure Information" page, which lists
/// statewide initiatives/referenda for the target year with PDF links for
/// ballot title letters and full text.
/// </summary>
public sealed class StatewideMeasuresScraper
{
    private readonly HttpFetcher _fetcher;
    private readonly WaSourceConfig _config;

    public StatewideMeasuresScraper(HttpFetcher fetcher, WaSourceConfig config)
    {
        _fetcher = fetcher;
        _config = config;
    }

    public async Task<List<MeasureRow>> FetchAsync(int year)
    {
        var html = await _fetcher.GetStringAsync(_config.StatewideMeasuresUrl);
        var doc = await new HtmlParser().ParseDocumentAsync(html);
        var baseUri = new Uri(_config.StatewideMeasuresUrl);

        var measures = new List<MeasureRow>();

        foreach (var heading in doc.QuerySelectorAll(Selectors.MeasureHeading))
        {
            var headingText = heading.TextContent.Trim();
            var match = Selectors.MeasureHeadingText.Match(headingText);
            if (!match.Success)
                continue;

            // Only keep measures under the section for the target year, when year
            // headings are present. Measure ids like "IL26-001" also embed the year.
            var sectionYear = FindSectionYear(heading);
            var idYearMatches = match.Groups["id"].Value.Contains((year % 100).ToString());
            if (sectionYear is int sy && sy != year && !idYearMatches)
                continue;

            string? fullTextUrl = null;
            string? titleLetterUrl = null;
            foreach (var link in CollectLinksUntilNextHeading(heading, baseUri))
            {
                if (Selectors.FullTextLink.IsMatch(link.Text))
                    fullTextUrl = link.Href;
                else
                    titleLetterUrl ??= link.Href;
            }

            measures.Add(new MeasureRow
            {
                ElectionDate = null, // proposed measures are not yet certified to a ballot
                MeasureId = match.Groups["id"].Value,
                Title = headingText,
                Summary = titleLetterUrl is null ? null : $"Ballot title letter: {titleLetterUrl}",
                FullTextUrl = fullTextUrl,
                Jurisdiction = "state",
                County = null,
                SourceUrl = _config.StatewideMeasuresUrl,
            });
        }

        if (measures.Count == 0)
            throw new InvalidOperationException(
                $"No statewide measures parsed from {_config.StatewideMeasuresUrl} using heading selector '{Selectors.MeasureHeading}'. " +
                "Either no measures are filed yet for the year or the page markup changed; verify the page manually.");

        return measures.OrderBy(m => m.MeasureId, StringComparer.Ordinal).ToList();
    }

    private static int? FindSectionYear(IElement measureHeading)
    {
        for (var node = measureHeading.PreviousElementSibling; node is not null; node = node.PreviousElementSibling)
        {
            if (node.TagName.Equals("H2", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(node.TextContent.Trim(), out var y))
                return y;
        }
        return null;
    }

    private static IEnumerable<(string Text, string Href)> CollectLinksUntilNextHeading(IElement heading, Uri baseUri)
    {
        for (var node = heading.NextElementSibling; node is not null; node = node.NextElementSibling)
        {
            if (node.TagName is "H2" or "H3")
                yield break;
            foreach (var a in node.QuerySelectorAll(Selectors.MeasurePdfLink))
            {
                var href = a.GetAttribute("href");
                if (!string.IsNullOrEmpty(href))
                    yield return (a.TextContent.Trim(), new Uri(baseUri, href).ToString());
            }
        }
    }
}
