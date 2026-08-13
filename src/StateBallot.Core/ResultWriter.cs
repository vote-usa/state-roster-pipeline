using System.Globalization;
using StateBallot.Core.Output;

namespace StateBallot.Core;

/// <summary>
/// Writes the state-agnostic JSON + CSV outputs and the sources.json provenance
/// manifest for a collector run. Output shapes are shared across all states.
/// </summary>
public sealed class ResultWriter
{
    private readonly string _outDir;

    public ResultWriter(string outDir) => _outDir = outDir;

    public void WriteAll(CollectResult result)
    {
        var electionRows = result.Elections.Select(ToElectionOut).ToList();
        OutputWriter.WriteJson(Path.Combine(_outDir, "elections.json"), electionRows);
        OutputWriter.WriteCsv(Path.Combine(_outDir, "elections.csv"), electionRows);

        var candidateRows = result.Candidates.Select(ToCandidateOut).ToList();
        OutputWriter.WriteJson(Path.Combine(_outDir, "candidates.json"), candidateRows);
        OutputWriter.WriteCsv(Path.Combine(_outDir, "candidates.csv"), candidateRows);

        var measureRows = result.StatewideProposedMeasures.Concat(result.Measures)
            .Select(ToMeasureOut).ToList();
        OutputWriter.WriteJson(Path.Combine(_outDir, "measures.json"), measureRows);
        OutputWriter.WriteCsv(Path.Combine(_outDir, "measures.csv"), measureRows);

        var directoryRows = result.CountyDirectory.Select(ToDirectoryOut).ToList();
        OutputWriter.WriteJson(Path.Combine(_outDir, "county_directory.json"), directoryRows);

        var ballotRows = result.CountyBallots.Select(ToBallotOut).ToList();
        OutputWriter.WriteJson(Path.Combine(_outDir, "county_ballots.json"), ballotRows);

        var flatBallotRows = result.CountyBallots.SelectMany(FlattenBallot).ToList();
        OutputWriter.WriteCsv(Path.Combine(_outDir, "county_ballots.csv"), flatBallotRows);

        OutputWriter.WriteJson(Path.Combine(_outDir, "sources.json"), result.Sources.ToJsonObject(result.Gaps));
    }

    public static ElectionOut ToElectionOut(Election e) => new()
    {
        State = e.State,
        ElectionDate = e.ElectionDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        ElectionType = e.ElectionType,
        Jurisdiction = e.Jurisdiction,
        OcdDivisionId = OcdDivisionId.ForElection(e.State, e.Jurisdiction, e.Name),
        Name = e.Name,
        ElectionId = e.ElectionId,
        SourceUrl = e.SourceUrl,
    };

    public static CandidateOut ToCandidateOut(CandidateRow c) => new()
    {
        State = c.State,
        ElectionDate = c.ElectionDate,
        ElectionType = c.ElectionType,
        Office = c.Office,
        District = c.District,
        County = c.County,
        OcdDivisionId = OcdDivisionId.ForCandidate(c.State, c.Office, c.District, c.County),
        CandidateName = c.CandidateName,
        Party = c.Party,
        Incumbent = c.Incumbent,
        SourceUrl = c.SourceUrl,
        SourceCandidateId = c.SourceCandidateId,
        FilingDate = c.FilingDate,
        Email = c.Email,
        Phone = c.Phone,
        CampaignPhone = c.CampaignPhone,
        Website = c.Website,
        Occupation = c.Occupation,
        MailingAddressLine = c.MailingAddressLine,
        MailingCity = c.MailingCity,
        MailingState = c.MailingState,
        MailingZip = c.MailingZip,
        ResidentialCity = c.ResidentialCity,
        ResidentialCounty = c.ResidentialCounty,
    };

    public static MeasureOut ToMeasureOut(MeasureRow m) => new()
    {
        State = m.State,
        ElectionDate = m.ElectionDate,
        MeasureId = m.MeasureId,
        Title = m.Title,
        Summary = m.Summary,
        FullTextUrl = m.FullTextUrl,
        Jurisdiction = m.Jurisdiction,
        County = m.County,
        OcdDivisionId = OcdDivisionId.ForMeasure(m.State, m.Jurisdiction, m.County),
        SourceUrl = m.SourceUrl,
    };

    public static CountyDirectoryOut ToDirectoryOut(CountyDirectoryRow d) => new()
    {
        State = d.State,
        CountyName = d.CountyName,
        CountyFips = d.CountyFips,
        OcdDivisionId = OcdDivisionId.ForCounty(d.State, d.CountyName),
        ElectionsOfficeUrl = d.ElectionsOfficeUrl,
        Address = d.Address,
        Phone = d.Phone,
    };

    public static CountyBallotOut ToBallotOut(CountyBallot b) => new()
    {
        State = b.State,
        County = b.CountyName,
        OcdDivisionId = OcdDivisionId.ForCounty(b.State, b.CountyName),
        ElectionDate = b.ElectionDate,
        ElectionType = b.ElectionType,
        Candidates = b.Candidates.Select(c => new CountyBallotCandidateOut
        {
            Office = c.Office,
            District = c.District,
            OcdDivisionId = OcdDivisionId.ForCandidate(c.State, c.Office, c.District, c.County),
            CandidateName = c.CandidateName,
            Party = c.Party,
        }).ToList(),
        Measures = b.Measures.Select(m => new CountyBallotMeasureOut
        {
            MeasureId = m.MeasureId,
            Title = m.Title,
            Summary = m.Summary,
            Jurisdiction = m.Jurisdiction,
            OcdDivisionId = OcdDivisionId.ForMeasure(m.State, m.Jurisdiction, m.County),
        }).ToList(),
        SourceUrl = b.SourceUrl,
    };

    private static IEnumerable<CountyBallotCsvOut> FlattenBallot(CountyBallot b)
    {
        foreach (var c in b.Candidates)
        {
            yield return new CountyBallotCsvOut
            {
                State = b.State,
                County = b.CountyName,
                OcdDivisionId = OcdDivisionId.ForCandidate(c.State, c.Office, c.District, c.County)
                               ?? OcdDivisionId.ForCounty(b.State, b.CountyName),
                ElectionDate = b.ElectionDate,
                ElectionType = b.ElectionType,
                EntryType = "candidate",
                Office = c.Office,
                District = c.District,
                CandidateName = c.CandidateName,
                Party = c.Party,
                SourceUrl = b.SourceUrl,
            };
        }

        foreach (var m in b.Measures)
        {
            yield return new CountyBallotCsvOut
            {
                State = b.State,
                County = b.CountyName,
                OcdDivisionId = OcdDivisionId.ForMeasure(m.State, m.Jurisdiction, m.County)
                               ?? OcdDivisionId.ForCounty(b.State, b.CountyName),
                ElectionDate = b.ElectionDate,
                ElectionType = b.ElectionType,
                EntryType = "measure",
                MeasureId = m.MeasureId,
                Title = m.Title,
                SourceUrl = b.SourceUrl,
            };
        }
    }
}
