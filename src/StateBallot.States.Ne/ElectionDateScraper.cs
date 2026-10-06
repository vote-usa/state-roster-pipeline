using StateBallot.Core;

namespace StateBallot.States.Ne;

/// <summary>
/// Parses the sos.nebraska.gov/elections page's own plain text for the
/// Primary/General dates - the statewide filing workbook (see NeCollector)
/// has no date column of its own for either sheet.
/// </summary>
public sealed class ElectionDateScraper
{
    private readonly HttpFetcher _fetcher;
    private readonly NeSourceConfig _config;
    private readonly string[] _dateFormats;

    /// <param name="dateFormats">Accepted date formats, from data/input/ne/date_formats.json.</param>
    public ElectionDateScraper(HttpFetcher fetcher, NeSourceConfig config, string[] dateFormats)
    {
        _fetcher = fetcher;
        _config = config;
        _dateFormats = dateFormats;
    }

    public async Task<(Election Primary, Election General)> FetchAsync(int year)
    {
        var html = await _fetcher.GetStringAsync(_config.ElectionsPageUrl);

        var primaryMatch = NeSelectors.PrimaryDate.Match(html);
        var generalMatch = NeSelectors.GeneralDate.Match(html);

        if (!primaryMatch.Success || !DateParsing.TryParseAny(primaryMatch.Groups["date"].Value, _dateFormats, out var primaryDate))
            throw new InvalidOperationException($"No Primary election date parsed from {_config.ElectionsPageUrl}.");
        if (!generalMatch.Success || !DateParsing.TryParseAny(generalMatch.Groups["date"].Value, _dateFormats, out var generalDate))
            throw new InvalidOperationException($"No General election date parsed from {_config.ElectionsPageUrl}.");

        if (primaryDate.Year != year || generalDate.Year != year)
            throw new InvalidOperationException(
                $"{_config.ElectionsPageUrl} currently lists {primaryDate.Year}/{generalDate.Year} election dates, not {year}. " +
                "The page may not have been updated for this year yet.");

        return (
            NeCandidateMapper.ToElection("Primary", primaryDate, _config.ElectionsPageUrl),
            NeCandidateMapper.ToElection("General", generalDate, _config.ElectionsPageUrl));
    }
}
