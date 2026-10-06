using StateBallot.Core;

namespace StateBallot.States.Wa;

/// <summary>Projects VoteWA voter-guide DTOs onto the canonical Core shapes.</summary>
public static class WaMapper
{
    public static CandidateRow ToCandidateRow(
        string stateCode, Election election, GuideCategory category, GuideRace race, GuideCandidate candidate,
        string? county, string sourceUrl, Selectors selectors, bool isStatewideCategory) => new()
    {
        State = stateCode,
        ElectionDate = election.ElectionDate.ToString("yyyy-MM-dd"),
        ElectionType = election.ElectionType,
        Office = race.Name?.Trim() ?? "",
        District = string.IsNullOrWhiteSpace(race.Jurisdiction) ? null : race.Jurisdiction.Trim(),
        County = county,
        CandidateName = candidate.BallotName!.Trim(),
        Party = VoterGuideClient.NormalizeParty(candidate.PartyName, selectors),
        Incumbent = null, // not published by VoteWA
        SourceUrl = sourceUrl,
        SourceElectionId = election.ElectionId,
        SourceOfficeId = string.IsNullOrWhiteSpace(race.RaceID) ? null : race.RaceID.Trim(),
        SourceOfficeType = string.IsNullOrWhiteSpace(category.CategoryCode) ? category.Name?.Trim() : category.CategoryCode.Trim(),
        LocalJurisdiction = isStatewideCategory || string.IsNullOrWhiteSpace(race.Jurisdiction) ? null : race.Jurisdiction.Trim(),
    };

    public static MeasureRow ToMeasureRow(
        string stateCode, Election election, GuideRace race, string? county, string sourceUrl) => new()
    {
        State = stateCode,
        ElectionDate = election.ElectionDate.ToString("yyyy-MM-dd"),
        SourceElectionId = election.ElectionId,
        MeasureId = race.RaceID ?? "",
        Title = (race.BallotTitle ?? race.MeasureName ?? race.Name ?? "").Trim(),
        Summary = VoterGuideClient.StripHtml(race.ShortDescription),
        FullTextUrl = null,
        Jurisdiction = string.IsNullOrWhiteSpace(race.Jurisdiction) ? "local" : race.Jurisdiction.Trim(),
        County = county,
        SourceUrl = sourceUrl,
    };
}
