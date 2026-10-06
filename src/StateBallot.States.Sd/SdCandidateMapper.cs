using StateBallot.Core;

namespace StateBallot.States.Sd;

/// <summary>Projects the SD VIP candidate CSV export rows onto the canonical Core shapes.</summary>
public static class SdCandidateMapper
{
    public static Election ToElection(string type, DateOnly date, string sourceUrl) => new()
    {
        State = "SD",
        ElectionId = $"{date.Year}-{type.ToLowerInvariant()}",
        Name = $"{date.Year} {type} Election",
        ElectionDate = date,
        ElectionType = type,
        Jurisdiction = "state",
        SourceUrl = sourceUrl,
    };

    /// <param name="row">One CSV row, keyed by SD's column headers (see DelimitedTableParser).</param>
    /// <param name="fieldMap">data/input/sd/candidate_field_map.json - canonical field -> SD's own column
    /// name, for the fields that are a plain single-column passthrough (see CandidateFieldMapper).</param>
    public static CandidateRow ToCandidateRow(
        Dictionary<string, string> row, Election election, string sourceUrl, Dictionary<string, string> fieldMap)
    {
        var office = row.GetValueOrDefault("Contest", "").Trim();
        var rawDistrictCounty = row.GetValueOrDefault("District/County", "").Trim();
        var (district, county) = SplitDistrictCounty(office, rawDistrictCounty);
        var (addressLine, state, zip) = SplitMailingAddress(row.GetValueOrDefault("Mailing Address", ""));

        return new CandidateRow
        {
            State = "SD",
            ElectionDate = election.ElectionDate.ToString("yyyy-MM-dd"),
            ElectionType = election.ElectionType,
            Office = office,
            District = district,
            County = county,
            CandidateName = row.GetValueOrDefault("Name", "").Trim(),
            Party = CandidateFieldMapper.Get(fieldMap, row, nameof(CandidateRow.Party)),
            Incumbent = null, // not published
            SourceUrl = sourceUrl,
            SourceCandidateId = null, // no source-provided id in this export
            FilingDate = CandidateFieldMapper.Get(fieldMap, row, nameof(CandidateRow.FilingDate)), // raw "M/d/yyyy h:mm:ss tt", not reformatted
            // No phone/email/website columns at all in this export - the
            // sparsest onboarded alongside WY/CO/SC's, minus even SC's own
            // Associated Counties/Status columns.
            Phone = null,
            CampaignPhone = null,
            Email = null,
            Website = null,
            // Street and city run together with no delimiter at all
            // ("2604 S Kierra Ct Sioux Falls") - not separable without a
            // city-name lookup this project doesn't have, so both stay
            // merged in MailingAddressLine; only the trailing state/zip are
            // reliably parseable off the end.
            MailingAddressLine = addressLine,
            MailingCity = null,
            MailingState = state,
            MailingZip = zip,
            ResidentialCity = null,
            ResidentialCounty = null,
            Status = null, // not published
        };
    }

    /// <summary>
    /// "District NN" (State Senate/House) -> bare number, no county. For a
    /// fixed set of real county offices (SdSelectors.CountyOffices):
    /// "{County}-{N}" (County Commissioner's own sub-district) splits both;
    /// otherwise the whole value is the county name, with any trailing
    /// " County" stripped for consistency regardless of which office's row
    /// it came from. Everything else non-blank (a city/ward/special-district
    /// name - not a real county) is kept verbatim in District, never guessed
    /// into County.
    /// </summary>
    private static (string? District, string? County) SplitDistrictCounty(string office, string rawValue)
    {
        if (rawValue.Length == 0)
            return (null, null);

        var numbered = SdSelectors.NumberedDistrict.Match(rawValue);
        if (numbered.Success)
            return (numbered.Groups["n"].Value, null);

        if (!SdSelectors.CountyOffices.Contains(office))
            return (rawValue, null);

        var subDistrict = SdSelectors.CountySubDistrict.Match(rawValue);
        return subDistrict.Success
            ? (subDistrict.Groups["n"].Value, StripCountySuffix(subDistrict.Groups["county"].Value))
            : ((string?)null, StripCountySuffix(rawValue));
    }

    private static string StripCountySuffix(string county) =>
        SdSelectors.CountySuffix.Replace(county.Trim(), "");

    private static (string? AddressLine, string? State, string? Zip) SplitMailingAddress(string rawAddress)
    {
        var trimmed = rawAddress.Trim();
        if (trimmed.Length == 0)
            return (null, null, null);

        var match = SdSelectors.TrailingStateZip.Match(trimmed);
        return match.Success
            ? (match.Groups["rest"].Value.Trim(), match.Groups["state"].Value, match.Groups["zip"].Value)
            : (trimmed, null, null);
    }
}
