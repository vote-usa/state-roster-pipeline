using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace StateBallot.Core;

/// <summary>
/// Generic HTML-&lt;table&gt; reader for states whose "export" is actually an
/// HTML table wearing a misleading extension - e.g. NM's "Excel (xls)" export
/// (a common Telerik RadGrid trick: a plain &lt;table&gt; served with an
/// `.xls` filename/content-type, not a real binary or OOXML spreadsheet).
/// Reading it as HTML rather than trusting the equivalent CSV export sidesteps
/// a real data-quality problem the CSV has: some fields (name suffixes, street
/// addresses) contain unescaped commas that silently shift every later column
/// on that row - HTML cells have no such ambiguity. Returns rows keyed by
/// header name (case-insensitive), same shape as
/// DelimitedTableParser/XlsxTableParser - a state-specific mapper still turns
/// named columns into canonical rows.
/// </summary>
public static class HtmlTableParser
{
    /// <param name="tableIndex">Zero-based index of the &lt;table&gt; to read, if a page has more than one (default: the first).</param>
    public static List<Dictionary<string, string>> Parse(string html, int tableIndex = 0)
    {
        var rows = new List<Dictionary<string, string>>();

        var document = new HtmlParser().ParseDocument(html);
        var table = document.QuerySelectorAll("table").ElementAtOrDefault(tableIndex);
        var trs = table?.QuerySelectorAll("tr").ToList();
        if (trs is null || trs.Count < 2)
            return rows; // no table, or header-only/empty

        var headers = trs[0].QuerySelectorAll("th, td").Select(CellText).ToList();

        foreach (var tr in trs.Skip(1))
        {
            var cells = tr.QuerySelectorAll("td, th").ToList();
            var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < headers.Count && i < cells.Count; i++)
            {
                if (headers[i].Length == 0)
                    continue;
                row[headers[i]] = CellText(cells[i]);
            }
            rows.Add(row);
        }

        return rows;
    }

    /// <summary>
    /// A cell's text, with each &lt;br&gt; read as a space rather than
    /// silently vanishing. Plain <c>TextContent</c> concatenates descendant
    /// text nodes with no separator at all for a &lt;br&gt; (it carries no
    /// text of its own) - real multi-line cell content (e.g. NM's own
    /// "Judicial Retention&lt;br /&gt;District Court Judge EIGHTH JUDICIAL
    /// DISTRICT" contest names) would otherwise get smashed into one
    /// unreadable word.
    /// </summary>
    private static string CellText(IElement cell)
    {
        var text = new System.Text.StringBuilder();
        foreach (var node in cell.ChildNodes)
        {
            if (node is IElement el && el.NodeName.Equals("br", StringComparison.OrdinalIgnoreCase))
                text.Append(' ');
            else
                text.Append(node.TextContent);
        }
        return TextNormalization.CollapseWhitespace(text.ToString());
    }
}
