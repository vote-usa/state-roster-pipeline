using System.Globalization;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using StateBallot.Core;

namespace StateBallot.States.Ca;

/// <summary>
/// Parses the SoS "Qualified Statewide Ballot Measures" page. The page has one
/// h2 per election ("November 3, 2026, Statewide Ballot Measures") followed by
/// pairs of paragraphs: a "Proposition N" heading, then either a single link to
/// the measure's full text (legislative measures) or a bold ballot title plus
/// summary text with a link to the initiative's full text (initiatives).
/// </summary>
public sealed class QualifiedMeasuresScraper
{
    private readonly HttpFetcher _fetcher;
    private readonly CaSourceConfig _config;

    public QualifiedMeasuresScraper(HttpFetcher fetcher, CaSourceConfig config)
    {
        _fetcher = fetcher;
        _config = config;
    }

    public async Task<List<MeasureRow>> FetchAsync()
    {
        var html = await _fetcher.GetStringAsync(_config.QualifiedMeasuresUrl);
        var doc = await new HtmlParser().ParseDocumentAsync(html);

        var measures = new List<MeasureRow>();
        DateOnly? currentElectionDate = null;

        var elements = doc.QuerySelectorAll("h2, p, ul, hr").ToList();
        for (var i = 0; i < elements.Count; i++)
        {
            var element = elements[i];
            var text = Normalize(element.TextContent);

            if (element.LocalName == "h2")
            {
                var headingMatch = CaSelectors.MeasuresElectionHeading.Match(text);
                if (headingMatch.Success &&
                    DateOnly.TryParseExact(headingMatch.Groups["date"].Value, "MMMM d, yyyy",
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                    currentElectionDate = date;
                continue;
            }

            if (currentElectionDate is null)
                continue;

            var propMatch = CaSelectors.PropositionHeading.Match(text);
            if (propMatch.Success)
            {
                // Multi-paragraph format with a standalone "Proposition N" heading, then the measure's content in the
                // following paragraphs/lists up to the next heading or divider.
                var content = new List<IElement>();
                for (var j = i + 1; j < elements.Count; j++)
                {
                    var next = elements[j];
                    var nextText = Normalize(next.TextContent);
                    if (next.LocalName is "h2" or "hr" || CaSelectors.PropositionHeading.IsMatch(nextText))
                        break;
                    if (nextText.Length == 0 || nextText.StartsWith("Note:", StringComparison.OrdinalIgnoreCase))
                        continue;
                    content.Add(next);
                }
                if (content.Count == 0)
                    continue;

                measures.Add(ParseMeasure($"Proposition {propMatch.Groups["num"].Value}", currentElectionDate.Value, content));
                continue;
            }

            // Single-paragraph format with one <p> per measure holding
            // both the bolded "Proposition N" heading and the linked title.
            if (element.QuerySelector("strong") is { } strong)
            {
                var inlineMatch = CaSelectors.PropositionHeading.Match(Normalize(strong.TextContent));
                if (inlineMatch.Success)
                {
                    strong.Remove();
                    measures.Add(ParseMeasure(
                        $"Proposition {inlineMatch.Groups["num"].Value}", currentElectionDate.Value, [element]));
                }
            }
        }

        if (measures.Count == 0)
            throw new InvalidOperationException(
                $"No qualified measures parsed from {_config.QualifiedMeasuresUrl} " +
                $"(heading pattern '{CaSelectors.MeasuresElectionHeading}', proposition pattern '{CaSelectors.PropositionHeading}'). " +
                "The page markup may have changed, or no measures have qualified yet.");

        return measures;
    }

    private MeasureRow ParseMeasure(string measureId, DateOnly electionDate, List<IElement> content)
    {
        var links = content
            .SelectMany(e => e.QuerySelectorAll("a[href]"))
            .Select(a => (Text: Normalize(a.TextContent), Href: a.GetAttribute("href")!))
            .Where(l => l.Href.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var first = content[0];
        var boldTitle = Normalize(first.QuerySelector("strong")?.TextContent ?? "");
        var firstText = Normalize(first.TextContent);
        string title;
        string? summary;
        string? fullTextUrl;

        if (boldTitle.Length > 0 && firstText.StartsWith(boldTitle, StringComparison.Ordinal))
        {
            // Initiative style: bold ballot title, then summary text (possibly
            // spanning paragraphs and bullet lists), with the Attorney General's
            // full-text PDF linked at the end.
            title = boldTitle.TrimEnd('.', ' ');
            var full = string.Join(" ", content.Select(e => Normalize(e.TextContent)));
            summary = Normalize(full[boldTitle.Length..]);
            if (summary.Length == 0) summary = null;
            fullTextUrl = links.Select(l => l.Href).LastOrDefault();
        }
        else if (links.Count > 0)
        {
            // Legislative style: a single link whose text is the measure title
            // and whose target is the chaptered bill text PDF.
            title = links[0].Text.TrimEnd();
            if (title.EndsWith("(PDF)", StringComparison.OrdinalIgnoreCase))
                title = title[..^"(PDF)".Length].TrimEnd();
            summary = null;
            fullTextUrl = links[0].Href;
        }
        else
        {
            title = firstText;
            summary = null;
            fullTextUrl = null;
        }

        return new MeasureRow
        {
            ElectionDate = electionDate.ToString("yyyy-MM-dd"),
            MeasureId = measureId,
            Title = title,
            Summary = summary,
            FullTextUrl = fullTextUrl,
            Jurisdiction = "state",
            County = null,
            SourceUrl = _config.QualifiedMeasuresUrl,
        };
    }

    private static string Normalize(string text) =>
        string.Join(' ', text.Replace('\u00a0', ' ').Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Trim();
}
