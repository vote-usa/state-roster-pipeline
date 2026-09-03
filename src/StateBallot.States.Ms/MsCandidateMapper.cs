using StateBallot.Core;

namespace StateBallot.States.Ms;

/// <summary>Projects the MS SOS Candidate Qualifying List CSV rows onto the canonical Core shapes.</summary>
public static class MsCandidateMapper
{
    private const string OfficePrefix = "Candidates for ";

    /// <param name="type">"Primary", "General", or "Special".</param>
    public static Election ToElection(string type, DateOnly date, string sourceUrl)
    {
        // "Special" elections are keyed by their own date rather than just the
        // year - unlike Primary/General (always exactly one of each per year in
        // this file), more than one distinct special-election date can appear
        // in the same year, and "{year}-special" would collide between them.
        var id = type.Equals("Special", StringComparison.OrdinalIgnoreCase)
            ? $"{date:yyyy-MM-dd}-special"
            : $"{date.Year}-{type.ToLowerInvariant()}";

        return new Election
        {
            State = "MS",
            ElectionId = id,
            Name = $"{date.Year} {type} Election",
            ElectionDate = date,
            ElectionType = type,
            Jurisdiction = "state",
            SourceUrl = sourceUrl,
        };
    }

    /// <summary>
    /// Attributes one CSV row to the election it actually belongs to. The file
    /// has two shapes of row:
    /// - Nonpartisan judicial races and active special elections carry their own
    ///   "Election" date directly - matched against the known elections by date.
    /// - Partisan federal races (US Senate/House) instead carry both a "Primary
    ///   Election" and a "General Election" date on every row, regardless of
    ///   party - this file is a live "currently qualified" snapshot, not a fixed
    ///   as-of-filing-deadline list, so before the primary it lists the full
    ///   field (still contesting the Primary) and after the primary it lists only
    ///   the resolved nominees plus any independent/minor-party candidate (MS
    ///   only runs state-administered primaries for the Democratic and
    ///   Republican parties; everyone else qualifies by petition straight onto
    ///   the November ballot - confirmed via sos.ms.gov's own qualifying-forms
    ///   page, which lists a separate "Independent Candidate Form" for exactly
    ///   this reason) - all now General candidates. Comparing today's date
    ///   against the row's own Primary date (rather than trusting Party alone,
    ///   which the live data does NOT reliably encode - independents/minor-party
    ///   rows carry the same populated Primary/General columns as Democratic/
    ///   Republican rows) correctly covers both states of the file.
    /// </summary>
    public static Election DetermineElection(
        Dictionary<string, string> row, IReadOnlyList<Election> elections, DateOnly today, string[] dateFormats)
    {
        var electionCol = row.GetValueOrDefault("Election", "").Trim();
        if (electionCol.Length > 0 && DateParsing.TryParseAny(electionCol, dateFormats, out var electionDate))
        {
            return elections.FirstOrDefault(e => e.ElectionDate == electionDate)
                ?? throw new InvalidOperationException($"No discovered election matches Election date '{electionCol}'.");
        }

        var primary = elections.First(e => e.ElectionType == "Primary");
        var general = elections.First(e => e.ElectionType == "General");
        return today > primary.ElectionDate ? general : primary;
    }

    /// <param name="row">One CSV row, keyed by MS's column headers (see DelimitedTableParser).</param>
    public static CandidateRow ToCandidateRow(Dictionary<string, string> row, Election election, string sourceUrl) => new()
    {
        State = "MS",
        ElectionDate = election.ElectionDate.ToString("yyyy-MM-dd"),
        ElectionType = election.ElectionType,
        Office = StripOfficePrefix(row.GetValueOrDefault("Office", "").Trim()),
        District = CombineDistrictAndPlace(row.GetValueOrDefault("District", ""), row.GetValueOrDefault("Place", "")),
        County = null, // not published - MS's judicial races use district/place numbering, not a county field
        CandidateName = row.GetValueOrDefault("Candidate Name", "").Trim(),
        Party = NullIfEmpty(row.GetValueOrDefault("Party")), // raw value (Democratic/Republican/Independent/Libertarian/blank for nonpartisan judicial races)
        Incumbent = null, // not published
        SourceUrl = sourceUrl,
        SourceCandidateId = null, // no source-provided id in this export
        FilingDate = NullIfEmpty(row.GetValueOrDefault("Qualifying Period")), // raw "M/d/yyyy to M/d/yyyy" range, not reformatted
    };

    private static string StripOfficePrefix(string office) =>
        office.StartsWith(OfficePrefix, StringComparison.Ordinal) ? office[OfficePrefix.Length..] : office;

    /// <summary>
    /// MS splits a judicial seat across two columns - District (e.g. "1", or a
    /// subdistrict like "5-1") and Place (a post/seat number within that
    /// district, e.g. "2") - both needed to identify the actual race (District 1
    /// Place 1 and District 1 Place 2 are different races). Merged into the one
    /// canonical District slot since the model has no separate Place field.
    /// </summary>
    private static string? CombineDistrictAndPlace(string district, string place)
    {
        district = district.Trim();
        place = place.Trim();
        if (district.Length == 0)
            return null;
        return place.Length == 0 ? district : $"{district} Place {place}";
    }

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
