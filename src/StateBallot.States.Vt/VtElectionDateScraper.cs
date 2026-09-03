using StateBallot.Core;

namespace StateBallot.States.Vt;

/// <summary>
/// Parses the VT SOS candidates page's own plain text for the Primary/General
/// dates - the candidate XLSX files (see VtCollector) have no date column of
/// their own; the whole file is one election's snapshot.
/// </summary>
public sealed class VtElectionDateScraper
{
    private readonly HttpFetcher _fetcher;
    private readonly VtSourceConfig _config;
    private readonly string[] _dateFormats;

    /// <param name="dateFormats">Accepted date formats, from data/input/vt/date_formats.json.</param>
    public VtElectionDateScraper(HttpFetcher fetcher, VtSourceConfig config, string[] dateFormats)
    {
        _fetcher = fetcher;
        _config = config;
        _dateFormats = dateFormats;
    }

    /// <summary>
    /// Returns null when the page doesn't show <paramref name="year"/>'s dates
    /// (it's an evergreen page, always reflecting the current cycle only -
    /// back-filling a past year isn't supported by this source).
    /// </summary>
    public async Task<(Election Primary, Election General)?> TryFetchAsync(int year)
    {
        var html = await _fetcher.GetStringAsync(_config.CandidatesPageUrl);

        var primaryMatch = VtSelectors.PrimaryElectionDateLine.Match(html);
        var generalMatch = VtSelectors.GeneralElectionDateLine.Match(html);

        if (!primaryMatch.Success || !DateParsing.TryParseAny(primaryMatch.Groups["date"].Value, _dateFormats, out var primaryDate))
            throw new InvalidOperationException($"No Primary election date parsed from {_config.CandidatesPageUrl}.");
        if (!generalMatch.Success || !DateParsing.TryParseAny(generalMatch.Groups["date"].Value, _dateFormats, out var generalDate))
            throw new InvalidOperationException($"No General election date parsed from {_config.CandidatesPageUrl}.");

        if (primaryDate.Year != year || generalDate.Year != year)
            return null;

        return (
            VtCandidateMapper.ToElection("Primary", primaryDate, _config.CandidatesPageUrl),
            VtCandidateMapper.ToElection("General", generalDate, _config.CandidatesPageUrl));
    }
}
