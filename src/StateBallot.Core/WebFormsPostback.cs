using System.Text.RegularExpressions;

namespace StateBallot.Core;

/// <summary>
/// Replays an ASP.NET WebForms postback for pages whose data-yielding action is
/// a real form submission, not a plain GET - e.g. a Telerik RadGrid "Export to
/// CSV" button (confirmed working this way for Hawaii's candidate filing page:
/// a genuine &lt;input type="submit"&gt;, not a client-side-only doPostBack, so
/// a plain form replay gets the full un-paginated export back directly, no
/// headless browser needed). Works directly against the raw HTML text rather
/// than a parsed DOM, so it has no dependency on any particular HTML parser.
/// </summary>
public static class WebFormsPostback
{
    private static readonly Regex HiddenInputTag = new(@"<input\b[^>]*>", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex TypeIsHidden = new(@"type\s*=\s*[""']hidden[""']", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex NameAttribute = new(@"name\s*=\s*[""']([^""']+)[""']", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private static readonly Regex ValueAttribute = new(@"value\s*=\s*[""']([^""']*)[""']", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    /// <summary>
    /// Every hidden &lt;input&gt; field's name/value on the page - the
    /// __VIEWSTATE/__EVENTVALIDATION/etc. a postback must replay verbatim.
    /// </summary>
    public static Dictionary<string, string> HiddenFields(string html)
    {
        var fields = new Dictionary<string, string>();
        foreach (Match tag in HiddenInputTag.Matches(html))
        {
            if (!TypeIsHidden.IsMatch(tag.Value))
                continue;
            var name = NameAttribute.Match(tag.Value);
            if (!name.Success)
                continue;
            var value = ValueAttribute.Match(tag.Value);
            fields[name.Groups[1].Value] = value.Success ? value.Groups[1].Value : "";
        }
        return fields;
    }

    /// <summary>
    /// Submits the page's own hidden fields back to itself with one extra
    /// empty-valued field added, simulating a click on the
    /// &lt;input type="submit" name="buttonName"&gt; button of that name.
    /// </summary>
    /// <param name="extraFields">
    /// Additional field name/value pairs to submit alongside the page's own
    /// hidden fields - e.g. a visible &lt;select&gt; the page needs set to a
    /// particular value before the button click is replayed (NM's export-
    /// format dropdown). Overrides a same-named hidden field, if any.
    /// </param>
    public static async Task<string> ClickButtonAsync(
        HttpFetcher fetcher, string url, string html, string buttonName,
        IReadOnlyDictionary<string, string>? extraFields = null)
    {
        var fields = HiddenFields(html);
        if (extraFields is not null)
            foreach (var (name, value) in extraFields)
                fields[name] = value;
        fields[buttonName] = "";
        return await fetcher.PostFormAsync(url, fields);
    }

    /// <summary>
    /// Replays a client-side-only postback that <see cref="ClickButtonAsync"/>
    /// can't trigger - a control wired to
    /// <c>javascript:__doPostBack('target','argument')</c> rather than being a
    /// genuine &lt;input type="submit"&gt; (confirmed for South Dakota's own
    /// Telerik RadGrid "Export to CSV" toolbar button: `type="button"` with an
    /// `onclick` handler, not `type="submit"` - HI's equivalent button happened
    /// to be the simpler shape). Submits the page's own hidden fields (which
    /// already include a blank `__EVENTTARGET`/`__EVENTARGUMENT` pair, since
    /// every WebForms page renders them) with those two overridden to the
    /// values that inline handler would have set immediately before calling
    /// `form.submit()`.
    /// </summary>
    public static async Task<string> TriggerPostbackAsync(
        HttpFetcher fetcher, string url, string html, string eventTarget, string eventArgument = "")
    {
        var fields = HiddenFields(html);
        fields["__EVENTTARGET"] = eventTarget;
        fields["__EVENTARGUMENT"] = eventArgument;
        return await fetcher.PostFormAsync(url, fields);
    }
}
