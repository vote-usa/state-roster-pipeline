using StateBallot.Core;

namespace StateBallot.States.Mt;

/// <summary>Projects the MT candidate filing portal's CSV export rows onto the canonical Core shapes.</summary>
public static class MtCandidateMapper
{
    public static Election ToElection(string type, DateOnly date, string sourceUrl) => new()
    {
        State = "MT",
        ElectionId = $"{date.Year}-{type.ToLowerInvariant()}",
        Name = $"{date.Year} {type} Election",
        ElectionDate = date,
        ElectionType = type,
        Jurisdiction = "state",
        SourceUrl = sourceUrl,
    };

    /// <param name="row">One CSV row, keyed by MT's column headers (see DelimitedTableParser).</param>
    /// <param name="fieldMap">data/input/mt/candidate_field_map.json - canonical field -> MT's own column
    /// name, for the fields that are a plain single-column passthrough (see CandidateFieldMapper).</param>
    public static CandidateRow ToCandidateRow(
        Dictionary<string, string> row, Election election, string sourceUrl, Dictionary<string, string> fieldMap)
    {
        var (office, district) = SplitOfficeDistrict(
            row.GetValueOrDefault("Race", "").Trim(),
            row.GetValueOrDefault("District Type", "").Trim(),
            row.GetValueOrDefault("District", "").Trim());
        var (email, website) = SplitEmailWebsite(row.GetValueOrDefault("Email/Web Address", ""));
        var (addressLine, city, state, zip) = SplitMailingAddress(row.GetValueOrDefault("Mailing Address", ""));

        return new CandidateRow
        {
            State = "MT",
            ElectionDate = election.ElectionDate.ToString("yyyy-MM-dd"),
            ElectionType = election.ElectionType,
            Office = office,
            District = district,
            County = null, // this portal has no county-level offices at all - see MtCollector
            CandidateName = row.GetValueOrDefault("Name", "").Trim(),
            Party = CandidateFieldMapper.Get(fieldMap, row, nameof(CandidateRow.Party)),
            Incumbent = null, // not published
            SourceUrl = sourceUrl,
            SourceCandidateId = null, // no source-provided id in this export
            FilingDate = CandidateFieldMapper.Get(fieldMap, row, nameof(CandidateRow.FilingDate)), // raw "M/d/yyyy h:mm:ss tt", not reformatted
            Phone = CandidateFieldMapper.Get(fieldMap, row, nameof(CandidateRow.Phone)),
            CampaignPhone = null, // export has only one generic phone column, not a labeled "campaign" one
            Email = email,
            Website = website,
            MailingAddressLine = addressLine,
            MailingCity = city,
            MailingState = state,
            MailingZip = zip,
            ResidentialCity = null,
            ResidentialCounty = null,
            Status = CandidateFieldMapper.Get(fieldMap, row, nameof(CandidateRow.Status)), // raw "FILED"/"WITHDRAWN"
        };
    }

    /// <summary>
    /// Dispatches on "District Type" - the one column with a fixed, reliable
    /// vocabulary - because where the actual district number lives varies by
    /// type in a way no single regex against "Race" alone can cover: it's
    /// absent from Race entirely for Congressional (both districts share the
    /// bare "UNITED STATES REPRESENTATIVE" Race text - the number is only in
    /// the "District" column, "1ST CONGRESSIONAL"), a "#N" seat suffix for
    /// Supreme Court Justice (not a geographic district), a "DISTRICT N[,
    /// DEPT M[ UNEXPIRED]]" suffix worth keeping whole for Judicial (more
    /// than one judge seat can share a numbered judicial district), and a
    /// plain trailing "[,] DISTRICT N" for everything else (House, Senate,
    /// Public Service Commission).
    /// </summary>
    private static (string Office, string? District) SplitOfficeDistrict(string race, string districtType, string rawDistrict)
    {
        switch (districtType)
        {
            case "Statewide":
                return (race, null);

            case "Congressional":
                var cd = MtSelectors.OrdinalCongressionalDistrict.Match(rawDistrict);
                return (race, cd.Success ? cd.Groups["n"].Value : null);

            case "Supreme Court Justice":
                var seat = MtSelectors.NumberedSeat.Match(race);
                return seat.Success ? (seat.Groups["office"].Value.Trim(), seat.Groups["n"].Value) : (race, null);

            case "Judicial":
                var judicial = MtSelectors.JudicialDistrict.Match(race);
                return judicial.Success ? (judicial.Groups["office"].Value.Trim(), judicial.Groups["rest"].Value.Trim()) : (race, null);

            default: // "House", "Senate", "Public Service Commission"
                var m = MtSelectors.TrailingDistrictNumber.Match(race);
                return m.Success ? (m.Groups["office"].Value.Trim(), m.Groups["n"].Value) : (race, null);
        }
    }

    /// <summary>
    /// "email@x.com&lt;br /&gt;WWW.X.COM" (email always first, confirmed live -
    /// every non-blank cell has exactly two "&lt;br&gt;"-joined parts) - the
    /// website slot's own sentinel values ("Not Provided", "WRITE-IN") never
    /// contain a literal ".", the one thing a real domain always has, so
    /// that's what distinguishes a real website from a placeholder rather
    /// than hardcoding the sentinel strings themselves.
    /// </summary>
    private static (string? Email, string? Website) SplitEmailWebsite(string raw)
    {
        if (raw.Length == 0)
            return (null, null);
        var parts = raw.Split("<br />", StringSplitOptions.TrimEntries);
        var email = parts.Length > 0 && parts[0].Contains('@') ? parts[0] : null;
        var website = parts.Length > 1 && parts[1].Contains('.') ? parts[1] : null;
        return (email, website);
    }

    /// <summary>
    /// A comma-joined "street[, city][, state][, zip]" cell read right-to-left,
    /// since city and/or state are each sometimes omitted (confirmed live:
    /// "3405 NORTH AVE W, MISSOULA, 59804" has no state; "31 WAVING GRASS
    /// WAY, MT, 59912" has no city) but never out of this relative order. A
    /// city is only extracted when a part is actually left over after zip and
    /// state are both accounted for - otherwise the sole remaining part could
    /// just as easily be the street with the city dropped (the MT case above),
    /// and guessing it's a city instead would be a "never invented" violation.
    /// </summary>
    private static (string? Line, string? City, string? State, string? Zip) SplitMailingAddress(string raw)
    {
        var parts = raw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList();
        if (parts.Count == 0)
            return (null, null, null, null);

        string? zip = null;
        if (MtSelectors.LeadingZipDigits.IsMatch(parts[^1]))
        {
            zip = parts[^1];
            parts.RemoveAt(parts.Count - 1);
        }

        string? state = null;
        if (parts.Count > 0 && MtSelectors.TwoLetterState.IsMatch(parts[^1]))
        {
            state = parts[^1];
            parts.RemoveAt(parts.Count - 1);
        }

        string? city = null;
        if (parts.Count > 1)
        {
            city = parts[^1];
            parts.RemoveAt(parts.Count - 1);
        }

        var line = parts.Count > 0 ? string.Join(", ", parts) : null;
        return (line, city, state, zip);
    }
}
