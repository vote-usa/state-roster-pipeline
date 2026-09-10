using System.Text.RegularExpressions;

namespace StateBallot.States.Sc;

/// <summary>Every regex/constant used against the SC Votes candidate portal, centralized so drift only requires updating this file.</summary>
public static class ScSelectors
{
    /// <summary>
    /// The two "General"-kind elections this collector cares about, matched
    /// by GetElections' own <c>electionName</c> field (stable across
    /// counties/years - unlike the exact ids, which change every cycle). The
    /// same "General" kind also returns any same-cycle special election
    /// (e.g. "US Senate Special Republican Primary") - deliberately not
    /// matched here, out of v1 scope.
    /// </summary>
    public const string GeneralElectionName = "Statewide General Election";
    public const string PrimaryElectionName = "Statewide Primary";

    /// <summary>
    /// Matches an Office cell ending in ", District N" - a consistent suffix
    /// across US House, State House, State Senate, and several (not all -
    /// SC's own county-by-county naming is inconsistent) local offices.
    /// Anything not matching this exact shape (e.g. "Solicitor Circuit 12",
    /// "County Council District 3" with no comma, "Trustee Seat 1") is kept
    /// verbatim in Office with a null District rather than guessed at.
    /// </summary>
    public static readonly Regex DistrictSuffix =
        new(@"^(?<office>.+?),\s*District\s+(?<n>\d+)$", RegexOptions.Compiled);
}
