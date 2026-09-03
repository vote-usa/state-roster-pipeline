using AngleSharp.Html.Parser;
using StateBallot.Core;

namespace StateBallot.States.Va;

/// <summary>
/// Virginia state collector, backed by the VA Dept. of Elections site. v1
/// scope: the general election's "All Offices" candidate list only - a
/// primary, when VA runs one, is published as separate per-party (Democratic/
/// Republican) and per-scope (federal/local) XLSX files with ad hoc,
/// hand-typed slugs and no consistent naming across cycles, unlike the single
/// general-election "All Offices" list; not attempted here. Neither the
/// index page nor any individual election page/XLSX filename is
/// year-templatable (filenames even carry ad hoc revision-date suffixes), so
/// everything is discovered by scraping the evergreen index page for the
/// current cycle's "All Offices" link, then that page for its own election
/// date (from its &lt;title&gt;) and linked XLSX - two HTML fetches before the
/// XLSX itself. The XLSX repeats every federal/statewide race once per
/// covered Virginia locality (of which there are ~133); VaCandidateMapper
/// collapses that back to one row per real candidacy.
/// </summary>
[StateCode("VA")]
public sealed class VaCollector : IStateCollector
{
    private readonly HttpFetcher _fetcher;
    private readonly VaSourceConfig _config;
    private readonly IPublishSchedule _schedule;
    private readonly int _year;
    private readonly string _stateDataDir;

    public string StateCode => "VA";

    /// <param name="stateDataDir">Per-state output directory (data/output/&lt;xx&gt;/). Inputs are under data/input/&lt;xx&gt;/,
    /// including date_formats.json.</param>
    public VaCollector(HttpFetcher fetcher, int year, string stateDataDir, VaSourceConfig? config = null)
    {
        _fetcher = fetcher;
        _year = year;
        _stateDataDir = stateDataDir;
        _config = config ?? new VaSourceConfig();
        _schedule = new VaPublishSchedule();
    }

    public async Task<CollectResult> CollectAsync()
    {
        Console.WriteLine($"Collecting Virginia ballot roster for {_year}...");

        var (dataRoot, _) = DataPaths.FromStateOutputDir(_stateDataDir);
        var dateFormats = DateFormatConfig.Load(DataPaths.DateFormatsPath(dataRoot, StateCode));
        var fieldMap = LookupTableLoader.Load(DataPaths.CandidateFieldMapPath(dataRoot, StateCode));

        var (electionPageUrl, electionPage) = await FindElectionPageAsync();

        var titleMatch = VaSelectors.TitleDate.Match(electionPage.Title ?? "");
        if (!titleMatch.Success || !DateParsing.TryParseAny(titleMatch.Groups["date"].Value, dateFormats, out var electionDate))
            throw new InvalidOperationException($"No election date parsed from {electionPageUrl}'s <title>.");
        if (electionDate.Year != _year)
            throw new InvalidOperationException(
                $"{electionPageUrl} shows a {electionDate.Year} date, not {_year}. The index page may not have been updated for this year yet.");

        var election = VaCandidateMapper.ToElection("General", electionDate, electionPageUrl);
        RowHelpers.StampState(election, StateCode);
        Console.WriteLine($"  Elections found: 1");

        var xlsxHref = electionPage.QuerySelector(VaSelectors.XlsxLinkCss)?.GetAttribute("href")
            ?? throw new InvalidOperationException($"No XLSX link found on {electionPageUrl}.");
        var xlsxUrl = new Uri(new Uri(electionPageUrl), xlsxHref).ToString();

        var result = new CollectResult();
        result.Elections.Add(election);

        var bytes = await _fetcher.GetBytesAsync(xlsxUrl);
        var rawRows = XlsxTableParser.Parse(bytes)
            .Where(r => !string.IsNullOrWhiteSpace(r.GetValueOrDefault("Office Title")))
            .Select(row => VaCandidateMapper.ToCandidateRow(row, election, xlsxUrl, fieldMap))
            .ToList();
        var candidates = VaCandidateMapper.MergeDuplicateLocalities(rawRows);

        if (candidates.Count == 0)
            throw new InvalidOperationException(
                $"No candidates parsed from {xlsxUrl}; refusing to write hollow outputs. Check {electionPageUrl} manually.");

        foreach (var candidate in candidates)
        {
            RowHelpers.StampState(candidate, StateCode);
            result.Candidates.Add(candidate);
        }

        Console.WriteLine($"  {election.Name} ({election.ElectionDate:yyyy-MM-dd}): {candidates.Count} candidates " +
            $"({rawRows.Count} rows before locality dedup) ({xlsxUrl})");

        CollectResultSorter.Sort(result);
        BuildSourcesManifest(result, electionPageUrl, xlsxUrl);
        return result;
    }

    /// <summary>Finds the index page's link to the requested year's "All Offices" general election page, and fetches/parses it.</summary>
    private async Task<(string Url, AngleSharp.Dom.IDocument Page)> FindElectionPageAsync()
    {
        var indexHtml = await _fetcher.GetStringAsync(_config.CandidateListIndexUrl);
        var indexDoc = await new HtmlParser().ParseDocumentAsync(indexHtml);

        foreach (var link in indexDoc.QuerySelectorAll("a"))
        {
            var text = TextNormalization.CollapseWhitespace(link.TextContent);
            var match = VaSelectors.AllOfficesLinkText.Match(text);
            if (!match.Success || match.Groups["year"].Value != _year.ToString())
                continue;

            var href = link.GetAttribute("href");
            if (string.IsNullOrWhiteSpace(href))
                continue;

            var url = new Uri(new Uri(_config.CandidateListIndexUrl), href).ToString();
            var html = await _fetcher.GetStringAsync(url);
            return (url, await new HtmlParser().ParseDocumentAsync(html));
        }

        throw new InvalidOperationException(
            $"{_config.CandidateListIndexUrl} has no '{_year} ... All Offices Candidate List' link. " +
            "This index only ever shows the current cycle - back-filling a past year isn't supported by this source.");
    }

    private void BuildSourcesManifest(CollectResult result, string electionPageUrl, string xlsxUrl)
    {
        var sources = result.Sources;
        sources.Elections = [new SourceEntry(electionPageUrl, "html")];
        sources.StatewideCandidates = [new SourceEntry(xlsxUrl, "xlsx")];
        sources.VerificationOnly = [new SourceEntry($"https://ballotpedia.org/Virginia_elections,_{_year}", "html")];
        sources.NextRun = _schedule.Recommend(result, _year);
    }
}
