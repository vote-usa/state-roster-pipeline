using StateBallot.Core;

namespace StateBallot.States.Ne;

/// <summary>Projects the NE SOS statewide filing workbook's candidate sheet (sheet 0) rows onto the canonical Core shapes.</summary>
public static class NeCandidateMapper
{
    /// <param name="type">"Primary" or "General", from the elections page text.</param>
    public static Election ToElection(string type, DateOnly date, string sourceUrl) => new()
    {
        State = "NE",
        ElectionId = $"{date.Year}-{type.ToLowerInvariant()}",
        Name = $"{date.Year} {type} Election",
        ElectionDate = date,
        ElectionType = type,
        Jurisdiction = "state",
        SourceUrl = sourceUrl,
    };

    /// <param name="row">One workbook row, keyed by NE's column headers (see XlsxTableParser).</param>
    public static CandidateRow ToCandidateRow(Dictionary<string, string> row, Election election, string sourceUrl)
    {
        var (mailingLine, city, state, zip) = SplitMailingAddress(row.GetValueOrDefault("Mailing Address", ""));
        var (phone, email) = SplitPhoneEmail(row.GetValueOrDefault("Phone/Email", ""));

        return new CandidateRow
        {
            State = "NE",
            ElectionDate = election.ElectionDate.ToString("yyyy-MM-dd"),
            ElectionType = election.ElectionType,
            Office = NeSelectors.OfficeForPrefix.Replace(row.GetValueOrDefault("Office", "").Trim(), "", 1),
            District = NullIfEmpty(row.GetValueOrDefault("District Name (if applicable)")),
            County = null, // not published - NE's sub-state jurisdictions here are legislative/judicial/special-district numbers, not counties
            CandidateName = TextNormalization.CollapseWhitespace(row.GetValueOrDefault("Candidate Name", "")),
            Party = NullIfEmpty(row.GetValueOrDefault("Party (if applicable)")), // blank for NE's nonpartisan races (Legislature, education/regents/community-college boards, special districts)
            Incumbent = row.GetValueOrDefault("Incumbency Status", "").Trim() switch
            {
                "Incumbent" => true,
                "Nonincumbent" => false,
                _ => null,
            },
            SourceUrl = sourceUrl,
            SourceCandidateId = null, // no source-provided id in this export
            FilingDate = null, // not published per-candidate in this export
            Email = email,
            Phone = phone,
            MailingAddressLine = mailingLine,
            MailingCity = city,
            MailingState = state,
            MailingZip = zip,
            ResidentialCity = NullIfEmpty(row.GetValueOrDefault("City of Residence")),
            // "Term" (years) and "Vote For" (seats up per race) are both published
            // columns with no matching CandidateRow field - not carried through
            // rather than overloading an unrelated field.
        };
    }

    /// <summary>
    /// The cell is newline-separated: an optional street-address line, then a
    /// single "City ST ZIP" line - or just "\n" (whitespace only) when no
    /// address is on file. Always exactly 0 or 2 non-empty lines in practice
    /// (never a bare city/state/zip with no street line), but this degrades
    /// gracefully to "keep the raw text, leave city/state/zip null" if that
    /// assumption is ever wrong rather than silently dropping data.
    /// </summary>
    private static (string? Line, string? City, string? State, string? Zip) SplitMailingAddress(string raw)
    {
        var lines = raw.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        if (lines.Count == 0)
            return (null, null, null, null);

        var cszMatch = NeSelectors.CityStateZip.Match(lines[^1]);
        if (!cszMatch.Success)
            return (string.Join(" ", lines), null, null, null);

        var addressLine = lines.Count > 1 ? string.Join(" ", lines[..^1]) : null;
        return (addressLine, cszMatch.Groups["city"].Value.Trim(), cszMatch.Groups["state"].Value, cszMatch.Groups["zip"].Value);
    }

    /// <summary>
    /// The cell combines both contact channels in one newline-separated value
    /// (phone line, then email line - either may be blank, and either may be
    /// the only line present at all) rather than separate columns - split by
    /// which line contains "@" instead of assuming a fixed line order.
    /// </summary>
    private static (string? Phone, string? Email) SplitPhoneEmail(string raw)
    {
        var lines = raw.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        var email = lines.FirstOrDefault(l => l.Contains('@'));
        var phone = lines.FirstOrDefault(l => !l.Contains('@'));
        return (phone, email);
    }

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
