using StateBallot.Core;

namespace StateBallot.States.Va;

/// <summary>Projects the VA DOE candidate XLSX rows onto the canonical Core shapes.</summary>
public static class VaCandidateMapper
{
    public static Election ToElection(string type, DateOnly date, string sourceUrl) => new()
    {
        State = "VA",
        ElectionId = $"{date.Year}-{type.ToLowerInvariant()}",
        Name = $"{date.Year} {type} Election",
        ElectionDate = date,
        ElectionType = type,
        Jurisdiction = "state",
        SourceUrl = sourceUrl,
    };

    /// <summary>
    /// Maps one XLSX row as-is - one row per (candidate, locality), exactly
    /// how the source publishes it. The export repeats every federal/statewide
    /// race once per covered locality (e.g. US Senate appears under all ~133
    /// localities); <see cref="MergeDuplicateLocalities"/> collapses those
    /// back down to one row per real candidacy afterward - kept as a separate
    /// pass rather than folded in here, so this function stays a pure
    /// row-to-row mapping like every other state's.
    /// </summary>
    /// <param name="row">One XLSX row, keyed by VA's column headers (see XlsxTableParser).</param>
    /// <param name="fieldMap">data/input/va/candidate_field_map.json - canonical field -> VA's own column
    /// name, for the fields that are a plain single-column passthrough (see CandidateFieldMapper).</param>
    public static CandidateRow ToCandidateRow(
        Dictionary<string, string> row, Election election, string sourceUrl, Dictionary<string, string> fieldMap)
    {
        var office = TextNormalization.CollapseWhitespace(row.GetValueOrDefault("Office Title", ""));
        var rawDistrict = TextNormalization.CollapseWhitespace(row.GetValueOrDefault("District", ""));
        var locality = TextNormalization.CollapseWhitespace(row.GetValueOrDefault("Locality", ""));
        var isWideOffice = VaSelectors.WideOffices.Contains(office);

        return new CandidateRow
        {
            State = "VA",
            ElectionDate = election.ElectionDate.ToString("yyyy-MM-dd"),
            ElectionType = election.ElectionType,
            Office = office,
            District = NormalizeDistrict(rawDistrict),
            // Federal/state-legislative races are never attributed to one
            // locality even though the row itself carries one - see
            // VaSelectors.WideOffices. Real local races keep it; multi-locality
            // ones (e.g. a town straddling two counties) get merged below.
            County = isWideOffice ? null : (locality.Length == 0 ? null : locality),
            CandidateName = TextNormalization.CollapseWhitespace(row.GetValueOrDefault("Candidate Name", "")),
            Party = CandidateFieldMapper.Get(fieldMap, row, nameof(CandidateRow.Party)),
            Incumbent = ParseIncumbent(row.GetValueOrDefault("Incumbent")),
            SourceUrl = sourceUrl,
            SourceCandidateId = null, // no source-provided id in this export
            FilingDate = null, // not published
            Phone = null, // no generic phone column - only the explicitly-campaign one below
            // VA's own column is explicitly labeled "Campaign Phone", not a
            // generic contact number (same idiom as WY's "Campaign Telephone").
            CampaignPhone = CandidateFieldMapper.Get(fieldMap, row, nameof(CandidateRow.CampaignPhone)),
            Email = CandidateFieldMapper.Get(fieldMap, row, nameof(CandidateRow.Email)),
            Website = CandidateFieldMapper.Get(fieldMap, row, nameof(CandidateRow.Website)),
            // Two separate address-line columns, CandidateRow has one slot -
            // joined rather than dropping line 2 (a suite/unit line in practice).
            MailingAddressLine = JoinAddressLines(row),
            MailingCity = CandidateFieldMapper.Get(fieldMap, row, nameof(CandidateRow.MailingCity)),
            MailingState = CandidateFieldMapper.Get(fieldMap, row, nameof(CandidateRow.MailingState)),
            MailingZip = CandidateFieldMapper.Get(fieldMap, row, nameof(CandidateRow.MailingZip)),
            ResidentialCity = null, // not published (only a campaign address, no separate residence)
            ResidentialCounty = null,
            Status = null, // not published
        };
    }

    /// <summary>
    /// Collapses the export's one-row-per-(candidate,locality) shape down to
    /// one row per real candidacy: a federal/statewide race (County already
    /// null from ToCandidateRow) simply dedupes to its first occurrence; a
    /// genuinely local race merges every distinct locality it was seen under
    /// into one "; "-joined County string (per the README's multi-county
    /// convention - VA has real examples, e.g. a town whose limits straddle
    /// two counties). Grouped by (Office, District, Party, CandidateName),
    /// since VA has no source-provided per-candidate id to group on instead.
    /// </summary>
    public static List<CandidateRow> MergeDuplicateLocalities(IEnumerable<CandidateRow> rows)
    {
        var merged = new List<CandidateRow>();
        foreach (var group in rows.GroupBy(r => (r.Office, r.District, r.Party, r.CandidateName)))
        {
            var first = group.First();
            var counties = group
                .Select(r => r.County)
                .Where(c => c is not null)
                .Distinct(StringComparer.Ordinal)
                .OrderBy(c => c, StringComparer.Ordinal)
                .ToList();
            first.County = counties.Count == 0 ? null : string.Join("; ", counties);
            merged.Add(first);
        }
        return merged;
    }

    /// <summary>"Statewide" -> null; an ordinal ("2nd District") -> its bare number; anything else kept verbatim.</summary>
    private static string? NormalizeDistrict(string rawDistrict)
    {
        if (rawDistrict.Length == 0 || VaSelectors.IsStatewideMarker(rawDistrict))
            return null;
        var match = VaSelectors.OrdinalDistrict.Match(rawDistrict);
        return match.Success ? match.Groups["n"].Value : rawDistrict;
    }

    private static bool? ParseIncumbent(string? raw) => raw?.Trim().ToUpperInvariant() switch
    {
        "YES" => true,
        "NO" => false,
        _ => null,
    };

    private static string? JoinAddressLines(Dictionary<string, string> row)
    {
        var line1 = row.GetValueOrDefault("Campaign Address Line 1", "").Trim();
        var line2 = row.GetValueOrDefault("Campaign Address Line 2", "").Trim();
        if (line1.Length == 0)
            return line2.Length == 0 ? null : line2;
        return line2.Length == 0 ? line1 : $"{line1}, {line2}";
    }
}
