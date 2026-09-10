using System.Globalization;
using StateBallot.Core.Output;
using StateBallot.Core.Publishing;

namespace StateBallot.Core;

/// <summary>
/// Writes a collector result as the election-scoped tree under a state directory
/// (see <see cref="ElectionTreeWriter"/>) and holds the row mappers shared by every writer.
/// </summary>
public sealed class ResultWriter
{
    private readonly string _stateDir;

    /// <param name="stateDir">Per-state output directory (data/output/&lt;xx&gt;/ or data-repo/&lt;xx&gt;/).</param>
    public ResultWriter(string stateDir) => _stateDir = stateDir;

    public ElectionTreeWriter.WriteReport WriteAll(CollectResult result, RunContext context) =>
        new ElectionTreeWriter(_stateDir).Write(result, context);

    public static ElectionOut ToElectionOut(Election e) => new()
    {
        State = e.State,
        ElectionDate = e.ElectionDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        ElectionType = ElectionTypes.Normalize(e.ElectionType),
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
        ElectionType = ElectionTypes.Normalize(c.ElectionType),
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
        SourceOfficeId = c.SourceOfficeId,
        SourceOfficeType = c.SourceOfficeType,
        LocalJurisdiction = c.LocalJurisdiction,
        FirstName = c.FirstName,
        MiddleName = c.MiddleName,
        LastName = c.LastName,
        Suffix = c.Suffix,
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
        ElectionType = ElectionTypes.Normalize(b.ElectionType),
        Candidates = b.Candidates.Select(c => new CountyBallotCandidateOut
        {
            Office = c.Office,
            District = c.District,
            OcdDivisionId = OcdDivisionId.ForCandidate(c.State, c.Office, c.District, c.County),
            CandidateName = c.CandidateName,
            Party = c.Party,
            SourceOfficeId = c.SourceOfficeId,
            SourceOfficeType = c.SourceOfficeType,
            LocalJurisdiction = c.LocalJurisdiction,
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
}
