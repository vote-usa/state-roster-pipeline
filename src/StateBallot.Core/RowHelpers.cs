namespace StateBallot.Core;

/// <summary>Small mutators for scrape-facing model rows.</summary>
public static class RowHelpers
{
    public static void StampState(Election row, string state) => row.State = state;
    public static void StampState(CandidateRow row, string state) => row.State = state;
    public static void StampState(MeasureRow row, string state) => row.State = state;
    public static void StampState(CountyDirectoryRow row, string state) => row.State = state;
    public static void StampState(CountyBallot row, string state) => row.State = state;

    public static void StampState(IEnumerable<Election> rows, string state)
    {
        foreach (var row in rows) StampState(row, state);
    }

    public static void StampState(IEnumerable<CandidateRow> rows, string state)
    {
        foreach (var row in rows) StampState(row, state);
    }

    public static void StampState(IEnumerable<MeasureRow> rows, string state)
    {
        foreach (var row in rows) StampState(row, state);
    }

    public static void StampState(IEnumerable<CountyDirectoryRow> rows, string state)
    {
        foreach (var row in rows) StampState(row, state);
    }

    /// <summary>Shallow copy of a candidate attributed to a single county.</summary>
    public static CandidateRow WithCounty(this CandidateRow c, string countyName) => new()
    {
        State = c.State,
        ElectionDate = c.ElectionDate,
        ElectionType = c.ElectionType,
        Office = c.Office,
        District = c.District,
        County = countyName,
        CandidateName = c.CandidateName,
        Party = c.Party,
        Incumbent = c.Incumbent,
        SourceUrl = c.SourceUrl,
        SourceCandidateId = c.SourceCandidateId,
        SourceElectionId = c.SourceElectionId,
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
}
