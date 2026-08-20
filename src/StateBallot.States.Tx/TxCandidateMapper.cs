using System.Globalization;
using StateBallot.Core;

namespace StateBallot.States.Tx;

/// <summary>Projects raw CivixApps DTOs onto the canonical Core shapes.</summary>
public static class TxCandidateMapper
{
    private static readonly Dictionary<string, string> ElectionTypeNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["P"] = "Primary",
        ["G"] = "General",
        ["S"] = "Special",
        ["R"] = "Runoff",
    };

    private static readonly string[] ElectionDateFormats = { "yyyy-MM-dd" };

    public static Election ToElection(TexasElection e)
    {
        var raw = e.DtElectionDate ?? throw new InvalidOperationException($"Election {e.IdElection} has no dtElectionDate");
        if (!DateParsing.TryParseAny(raw, ElectionDateFormats, out var date))
            throw new InvalidOperationException(
                $"Election {e.IdElection} has an unrecognized dtElectionDate format: '{raw}'. Expected 'yyyy-MM-dd'.");

        return new Election
        {
            State = "TX",
            ElectionId = e.IdElection.ToString(CultureInfo.InvariantCulture),
            Name = e.TxElectionName ?? "",
            ElectionDate = date,
            ElectionType = NormalizeElectionType(e.CdElectionType),
            SourceUrl = "",
        };
    }

    private static string NormalizeElectionType(string? code) =>
        code is not null && ElectionTypeNames.TryGetValue(code, out var name) ? name : code ?? "";

    public static CandidateRow ToCandidateRow(TexasCandidate c, Election election, string sourceUrl) => new()
    {
        State = "TX",
        ElectionDate = election.ElectionDate.ToString("yyyy-MM-dd"),
        ElectionType = election.ElectionType,
        Office = c.TxOfficeName ?? "",
        District = null, // not exposed as a discrete field; already embedded in office name text where applicable
        County = null, // CivixApps does not attribute candidates to counties
        CandidateName = c.TxFullNameBallot ?? "",
        Party = c.CdParty, // raw source code (e.g. "R"/"D") - not renamed, to avoid inventing a mapping TX doesn't publish
        Incumbent = null, // not published
        SourceUrl = sourceUrl,
        SourceCandidateId = c.IdCandidate.ToString(CultureInfo.InvariantCulture),
        FilingDate = c.DtFiled,
        Email = c.TxEmail,
        Occupation = c.TxOccupation,
        MailingAddressLine = FormatMailingLine(c.MailingAddress),
        MailingCity = c.MailingAddress?.TxCity,
        MailingState = c.MailingAddress?.CdState,
        MailingZip = c.MailingAddress?.TxZip5,
    };

    /// <summary>Composite key TX candidates are considered duplicates by: name, occupation, filed date.</summary>
    public static object DeduplicationKey(TexasCandidate c) =>
        (
            (c.TxFullNameBallot ?? "").Trim().ToUpperInvariant(),
            (c.TxOccupation ?? "").Trim().ToUpperInvariant(),
            (c.DtFiled ?? "").Trim()
        );

    private static string? FormatMailingLine(TexasMailingAddress? address) =>
        address is null
            ? null
            : AddressFormatting.FormatMailingLine(address.TxStreetNumber, address.TxStreetName, address.TxStreetName2);
}
