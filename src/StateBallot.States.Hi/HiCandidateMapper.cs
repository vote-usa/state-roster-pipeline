using StateBallot.Core;

namespace StateBallot.States.Hi;

/// <summary>Projects the HI Office of Elections candidate export rows onto the canonical Core shapes.</summary>
public static class HiCandidateMapper
{
    /// <param name="type">"Primary" or "General".</param>
    public static Election ToElection(string type, DateOnly date, string sourceUrl) => new()
    {
        State = "HI",
        ElectionId = $"{date.Year}-{type.ToLowerInvariant()}",
        Name = $"{date.Year} {type} Election",
        ElectionDate = date,
        ElectionType = type,
        Jurisdiction = "state",
        SourceUrl = sourceUrl,
    };

    /// <summary>
    /// HI's single candidate export covers the whole cycle - every row's own
    /// Status says which election it's actually in, rather than a per-row
    /// election date/id the way most other states' sources work. "In General"
    /// is the only status that unambiguously means the candidate advanced to
    /// November; everything else (Issued, Filed, In Primary, Withdrawn, Void,
    /// Elected After Primary - the last meaning the race was decided outright
    /// in the primary, common for HI's nonpartisan county races) reflects an
    /// outcome reached no later than the primary, so is attributed there.
    /// </summary>
    public static Election DetermineElection(string status, Election primary, Election general) =>
        status.Contains("General", StringComparison.OrdinalIgnoreCase) ? general : primary;

    /// <param name="row">One CSV row, keyed by HI's column headers (see DelimitedTableParser).</param>
    /// <param name="fieldMap">data/input/hi/candidate_field_map.json - canonical field -> HI's own column
    /// name, for the fields that are a plain single-column passthrough (see CandidateFieldMapper).</param>
    public static CandidateRow ToCandidateRow(
        Dictionary<string, string> row, Election election, string sourceUrl, Dictionary<string, string> fieldMap)
    {
        var (office, district) = SplitContest(row.GetValueOrDefault("Contests", "").Trim());
        var cszMatch = HiSelectors.CityStateZip.Match(row.GetValueOrDefault("CityStateZip", ""));

        return new CandidateRow
        {
            State = "HI",
            ElectionDate = election.ElectionDate.ToString("yyyy-MM-dd"),
            ElectionType = election.ElectionType,
            Office = office,
            District = district,
            County = null, // HI's export has no county concept (its "counties" are the county-level offices/contests themselves)
            CandidateName = row.GetValueOrDefault("BallotName", "").Trim(),
            Party = CandidateFieldMapper.Get(fieldMap, row, nameof(CandidateRow.Party)), // raw value (DEMOCRATIC/REPUBLICAN/NONPARTISAN/GREEN/LIBERTARIAN/...)
            Incumbent = null, // not published
            SourceUrl = sourceUrl,
            SourceCandidateId = null, // no source-provided id in this export
            FilingDate = CandidateFieldMapper.Get(fieldMap, row, nameof(CandidateRow.FilingDate)), // raw "M/d/yyyy 12:00:00 AM", not reformatted
            Email = CandidateFieldMapper.Get(fieldMap, row, nameof(CandidateRow.Email)),
            Phone = CandidateFieldMapper.Get(fieldMap, row, nameof(CandidateRow.Phone)),
            Website = CandidateFieldMapper.Get(fieldMap, row, nameof(CandidateRow.Website)),
            MailingAddressLine = CandidateFieldMapper.Get(fieldMap, row, nameof(CandidateRow.MailingAddressLine)),
            MailingCity = cszMatch.Success ? cszMatch.Groups["city"].Value.Trim() : null,
            MailingState = cszMatch.Success ? cszMatch.Groups["state"].Value : null,
            MailingZip = cszMatch.Success ? cszMatch.Groups["zip"].Value : null,
            // raw: Issued/Filed/In Primary/In General/Withdrawn/Void/Elected After Primary
            Status = CandidateFieldMapper.Get(fieldMap, row, nameof(CandidateRow.Status)),
        };
    }

    private static (string Office, string? District) SplitContest(string contest)
    {
        var match = HiSelectors.ContestWithDistrict.Match(contest);
        return match.Success
            ? (match.Groups["office"].Value.Trim(), match.Groups["district"].Value.Trim())
            : (contest, null);
    }
}
