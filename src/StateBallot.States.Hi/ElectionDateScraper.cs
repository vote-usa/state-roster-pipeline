using StateBallot.Core;

namespace StateBallot.States.Hi;

/// <summary>
/// Parses the elections.hawaii.gov home page's "20NN Elections" widget for the
/// Primary/General dates - HI's candidate export (see CandidateExportClient)
/// has no date column of its own, only a per-row Status that says which of the
/// two elections a candidate is actually in.
/// </summary>
public sealed class ElectionDateScraper
{
    private readonly HttpFetcher _fetcher;
    private readonly HiSourceConfig _config;
    private readonly string[] _dateFormats;

    /// <param name="dateFormats">Accepted date formats, from data/input/hi/date_formats.json.</param>
    public ElectionDateScraper(HttpFetcher fetcher, HiSourceConfig config, string[] dateFormats)
    {
        _fetcher = fetcher;
        _config = config;
        _dateFormats = dateFormats;
    }

    public async Task<(Election Primary, Election General)> FetchAsync(int year)
    {
        var html = await _fetcher.GetStringAsync(_config.ElectionsHomeUrl);

        var primaryMatch = HiSelectors.PrimaryDate.Match(html);
        var generalMatch = HiSelectors.GeneralDate.Match(html);

        if (!primaryMatch.Success || !DateParsing.TryParseAny(primaryMatch.Groups["date"].Value, _dateFormats, out var primaryDate))
            throw new InvalidOperationException($"No Primary election date parsed from {_config.ElectionsHomeUrl}.");
        if (!generalMatch.Success || !DateParsing.TryParseAny(generalMatch.Groups["date"].Value, _dateFormats, out var generalDate))
            throw new InvalidOperationException($"No General election date parsed from {_config.ElectionsHomeUrl}.");

        if (primaryDate.Year != year || generalDate.Year != year)
            throw new InvalidOperationException(
                $"{_config.ElectionsHomeUrl} currently lists {primaryDate.Year}/{generalDate.Year} election dates, not {year}. " +
                "The page may not have been updated for this year yet.");

        return (
            HiCandidateMapper.ToElection("Primary", primaryDate, _config.ElectionsHomeUrl),
            HiCandidateMapper.ToElection("General", generalDate, _config.ElectionsHomeUrl));
    }
}
