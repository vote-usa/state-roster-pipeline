using System.Globalization;
using StateBallot.Core;

namespace StateBallot.States.Wv;

/// <summary>Projects raw WV SOS DTOs onto the canonical Core shapes.</summary>
public static class WvCandidateMapper
{
    private static readonly string[] KnownDateFormats = { "yyyy-MM-dd", "yyyy-MM-ddTHH:mm:ss", "M/d/yyyy", "MM/dd/yyyy" };

    /// <summary>
    /// WV's candidate API has no standalone election-catalog endpoint - each candidate
    /// record carries its own election fields, so an Election is derived from one
    /// representative candidate in each (electionId) group.
    /// </summary>
    public static Election ToElection(WestVirginiaCandidate c) => new()
    {
        State = "WV",
        ElectionId = c.ElectionId.ToString(CultureInfo.InvariantCulture),
        Name = c.ElectionName ?? "",
        ElectionDate = ParseElectionDate(c.ElectionDate, c.ElectionId),
        ElectionType = c.ElectionType ?? "",
        SourceUrl = "",
    };

    public static CandidateRow ToCandidateRow(WestVirginiaCandidate c, Election election, string sourceUrl) => new()
    {
        State = "WV",
        ElectionDate = election.ElectionDate.ToString("yyyy-MM-dd"),
        ElectionType = election.ElectionType,
        Office = c.OfficeName ?? "",
        District = string.IsNullOrWhiteSpace(c.CandidateDistrictName) ? null : c.CandidateDistrictName.Trim(),
        County = c.ResidentialAddress?.CountyDescription,
        CandidateName = c.CandidateBallotName ?? BuildFullName(c),
        Party = c.PartyDescription ?? c.PartyCode,
        Incumbent = null, // not published
        SourceUrl = sourceUrl,
        SourceCandidateId = c.CandidateId.ToString(CultureInfo.InvariantCulture),
        FilingDate = c.FilingDate,
        Email = c.CandidateEmail,
        Phone = c.CandidatePhoneNumber,
        CampaignPhone = c.CampaignPhoneNumber,
        Website = c.Website,
        MailingAddressLine = FormatMailingLine(c.MailingAddress),
        MailingCity = c.MailingAddress?.City,
        MailingState = c.MailingAddress?.State,
        MailingZip = c.MailingAddress?.Zip5,
        ResidentialCity = c.ResidentialAddress?.City,
        ResidentialCounty = c.ResidentialAddress?.CountyDescription,
    };

    /// <summary>Composite key WV candidates are considered duplicates by: id, name, election, office, filing date.</summary>
    public static object DeduplicationKey(WestVirginiaCandidate c) =>
        (c.CandidateId, c.CandidateBallotName, c.ElectionId, c.OfficeId, c.FilingDate);

    private static string BuildFullName(WestVirginiaCandidate c)
    {
        var parts = new[] { c.CandidateFirstName, c.CandidateMiddleName, c.CandidateLastName, c.CandidateSuffixName }
            .Where(p => !string.IsNullOrWhiteSpace(p));
        return string.Join(" ", parts);
    }

    private static DateOnly ParseElectionDate(string? raw, int electionId)
    {
        if (raw is not null && DateOnly.TryParseExact(raw, KnownDateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return date;

        throw new InvalidOperationException(
            $"Election {electionId} has an unrecognized electionDate format: '{raw}'. " +
            $"Expected one of: {string.Join(", ", KnownDateFormats)}.");
    }

    private static string? FormatMailingLine(WestVirginiaAddress? address)
    {
        if (address is null)
            return null;

        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(address.StreetNumber))
            parts.Add(address.StreetNumber);
        if (!string.IsNullOrWhiteSpace(address.Street1))
            parts.Add(address.Street1);
        if (!string.IsNullOrWhiteSpace(address.Street2))
            parts.Add(address.Street2);

        return parts.Count == 0 ? null : string.Join(" ", parts);
    }
}
