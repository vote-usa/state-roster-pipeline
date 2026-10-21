using StateBallot.Core;
using StateBallot.Core.Raw;

namespace StateBallot.States.Ms;

/// <summary>
/// Mississippi state collector, backed by the SOS's single "Candidate
/// Qualifying List" export - an ASP.NET WebForms page whose "Download CSV"
/// button (see WebFormsPostback, same mechanism as HI) returns the state's
/// full current candidate roster in one response: U.S. Senate/House,
/// nonpartisan judicial races (Court of Appeals, Chancery Court, Circuit
/// Court), and any active special elections, all in one file with no
/// separate per-election fetch. See MsSourceConfig for why this source needs
/// a curl-like User-Agent rather than TX's browser-spoofing headers. v1
/// scope: candidates only - no county concept in this export (judicial races
/// carry their own district/place numbering instead) and no ballot-measure
/// source attempted.
/// </summary>
[StateCode("MS")]
public sealed class MsCollector(int year, string stateDataDir, string? inputDataRoot = null, MsSourceConfig? config = null, SourceLinkSet? links = null)
    : StateCollectorBase<MsSourceConfig>(year, stateDataDir, inputDataRoot, config, links)
{
    protected override IPublishSchedule Schedule { get; } = new MsPublishSchedule();

    public const string QualifyingListPageRole = "qualifying-list-page";
    public const string CandidateExportRole = "candidate-export";

    public override async Task CaptureAsync(HttpFetcher fetcher, DateOnly asOf)
    {
        Console.WriteLine($"Capturing Mississippi sources for {Year}...");

        // The Akamai WAF in front of sos.ms.gov blocks realistic browser UAs
        // (and HttpFetcher's own default) but lets plain HTTP-tool UAs like
        // curl through untouched - see MsSourceConfig. Assumes one HttpFetcher
        // per single-state run (same caveat as TxCollector's header stamping).
        foreach (var (name, value) in MsSourceConfig.ExtraHeaders)
            fetcher.AddDefaultHeader(name, value);

        var url = Config.CandidateQualifyingListUrl;
        var html = await fetcher.GetStringAsync(url, FetchTag.Of(QualifyingListPageRole));
        await WebFormsPostback.ClickButtonAsync(
            fetcher, url, html, MsSourceConfig.DownloadCsvButtonName, tag: FetchTag.Of(CandidateExportRole));
    }

    protected override CollectResult NormalizeCore(CaptureReader capture)
    {
        Console.WriteLine($"Normalizing Mississippi capture {capture.CaptureId} for {Year}...");

        var url = Config.CandidateQualifyingListUrl;
        var csvText = capture.Require(CandidateExportRole).Text();

        var rows = DelimitedTableParser.Parse(csvText)
            .Where(r => r.GetValueOrDefault("Candidate Name", "").Trim().Length > 0) // drop the trailing empty placeholder row
            .ToList();
        ScrapeGuard.RequireAny(rows, () => $"No candidate rows parsed from the CSV export at {url}.");

        var elections = DiscoverElections(rows, DateFormats, url);
        Console.WriteLine($"  Elections found in the file: {elections.Count}");
        RowHelpers.StampState(elections, StateCode);

        var result = new CollectResult();
        result.Elections.AddRange(elections);

        var today = capture.AsOf;
        foreach (var row in rows)
        {
            var election = MsCandidateMapper.DetermineElection(row, elections, today, DateFormats);
            var candidate = MsCandidateMapper.ToCandidateRow(row, election, url, FieldMap);
            RowHelpers.StampState(candidate, StateCode);
            result.Candidates.Add(candidate);
        }

        foreach (var election in elections)
        {
            var count = result.Candidates.Count(c => c.ElectionDate == election.ElectionDate.ToString("yyyy-MM-dd"));
            Console.WriteLine($"  {election.Name} ({election.ElectionDate:yyyy-MM-dd}): {count} candidates");
        }

        BuildSourcesManifest(result, url);
        return result;
    }

    /// <summary>
    /// Every row carries a Primary Election and General Election date (federal
    /// partisan races) or a single Election date (nonpartisan judicial races and
    /// specials) - see MsCandidateMapper.DetermineElection for how a row is
    /// attributed to one of these. Primary/General are expected to appear
    /// exactly once each across the whole file; any distinct Election-column
    /// date beyond those two is a genuinely separate special election.
    /// </summary>
    private static List<Election> DiscoverElections(List<Dictionary<string, string>> rows, string[] dateFormats, string sourceUrl)
    {
        var primaryDate = ParseFirst(rows, "Primary Election", dateFormats)
            ?? throw new InvalidOperationException($"No parseable 'Primary Election' date found in {sourceUrl}.");
        var generalDate = ParseFirst(rows, "General Election", dateFormats)
            ?? throw new InvalidOperationException($"No parseable 'General Election' date found in {sourceUrl}.");

        var elections = new List<Election>
        {
            MsCandidateMapper.ToElection("Primary", primaryDate, sourceUrl),
            MsCandidateMapper.ToElection("General", generalDate, sourceUrl),
        };

        var specialDates = rows
            .Select(r => r.GetValueOrDefault("Election", ""))
            .Where(d => d.Length > 0)
            .Select(d => DateParsing.TryParseAny(d, dateFormats, out var date) ? date : (DateOnly?)null)
            .Where(d => d is not null && d != primaryDate && d != generalDate)
            .Select(d => d!.Value)
            .Distinct()
            .OrderBy(d => d);

        elections.AddRange(specialDates.Select(d => MsCandidateMapper.ToElection("Special", d, sourceUrl)));
        return elections;
    }

    private static DateOnly? ParseFirst(List<Dictionary<string, string>> rows, string column, string[] dateFormats) =>
        rows
            .Select(r => r.GetValueOrDefault(column, ""))
            .Where(d => d.Length > 0)
            .Select(d => DateParsing.TryParseAny(d, dateFormats, out var date) ? date : (DateOnly?)null)
            .FirstOrDefault(d => d is not null);

    private void BuildSourcesManifest(CollectResult result, string url)
    {
        var sources = result.Sources;
        sources.Elections = [new SourceEntry(url, "csv (via WebForms POST)")];
        sources.StatewideCandidates = [new SourceEntry(url, "csv (via WebForms POST)")];
        sources.VerificationOnly = Config.VerificationSources(Year);
    }
}
