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
        var electionRows = result.Elections.Select(e => new
        {
            state = e.State,
            election_date = e.ElectionDate.ToString("yyyy-MM-dd"),
            election_type = e.ElectionType,
            jurisdiction = e.Jurisdiction,
            ocd_division_id = OcdDivisionId.ForElection(e.State, e.Jurisdiction, e.Name),
            name = e.Name,
            election_id = e.ElectionId,
            source_url = e.SourceUrl,
        }).ToList();
        OutputWriter.WriteJson(Path.Combine(_outDir, "elections.json"), electionRows);
        OutputWriter.WriteCsv(Path.Combine(_outDir, "elections.csv"), electionRows);

        var candidateRows = result.Candidates.Select(c => new
        {
            state = c.State,
            election_date = c.ElectionDate,
            election_type = c.ElectionType,
            office = c.Office,
            district = c.District,
            county = c.County,
            ocd_division_id = OcdDivisionId.ForCandidate(c.State, c.Office, c.District, c.County),
            candidate_name = c.CandidateName,
            party = c.Party,
            incumbent = c.Incumbent,
            source_url = c.SourceUrl,
        }).ToList();
        OutputWriter.WriteJson(Path.Combine(_outDir, "candidates.json"), candidateRows);
        OutputWriter.WriteCsv(Path.Combine(_outDir, "candidates.csv"), candidateRows);

        var measureRows = result.StatewideProposedMeasures.Concat(result.Measures).Select(m => new
        {
            state = m.State,
            election_date = m.ElectionDate,
            measure_id = m.MeasureId,
            title = m.Title,
            summary = m.Summary,
            full_text_url = m.FullTextUrl,
            jurisdiction = m.Jurisdiction,
            county = m.County,
            ocd_division_id = OcdDivisionId.ForMeasure(m.State, m.Jurisdiction, m.County),
            source_url = m.SourceUrl,
        }).ToList();
        OutputWriter.WriteJson(Path.Combine(_outDir, "measures.json"), measureRows);
        OutputWriter.WriteCsv(Path.Combine(_outDir, "measures.csv"), measureRows);

        var directoryRows = result.CountyDirectory.Select(d => new
        {
            state = d.State,
            county_name = d.CountyName,
            county_fips = d.CountyFips,
            ocd_division_id = OcdDivisionId.ForCounty(d.State, d.CountyName),
            elections_office_url = d.ElectionsOfficeUrl,
            address = d.Address,
            phone = d.Phone,
        }).ToList();
        OutputWriter.WriteJson(Path.Combine(_outDir, "county_directory.json"), directoryRows);

        OutputWriter.WriteJson(Path.Combine(_outDir, "county_ballots.json"), result.CountyBallots.Select(b => new
        {
            state = b.State,
            county = b.CountyName,
            ocd_division_id = OcdDivisionId.ForCounty(b.State, b.CountyName),
            election_date = b.ElectionDate,
            election_type = b.ElectionType,
            candidates = b.Candidates.Select(c => new
            {
                office = c.Office,
                district = c.District,
                ocd_division_id = OcdDivisionId.ForCandidate(c.State, c.Office, c.District, c.County),
                candidate_name = c.CandidateName,
                party = c.Party,
            }),
            measures = b.Measures.Select(m => new
            {
                measure_id = m.MeasureId,
                title = m.Title,
                summary = m.Summary,
                jurisdiction = m.Jurisdiction,
                ocd_division_id = OcdDivisionId.ForMeasure(m.State, m.Jurisdiction, m.County),
            }),
            source_url = b.SourceUrl,
        }).ToList());

        // Flat CSV: one row per candidate or measure per county ballot.
        var flatBallotRows = result.CountyBallots.SelectMany(b =>
            b.Candidates.Select(c => new CountyBallotCsvRow
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
            }).Concat(b.Measures.Select(m => new CountyBallotCsvRow
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
            }))).ToList();
        OutputWriter.WriteCsv(Path.Combine(_outDir, "county_ballots.csv"), flatBallotRows);

        var sources = new Dictionary<string, object?>(result.SourceGroups)
        {
            ["gaps"] = result.Gaps,
            ["next_run"] = result.NextRun,
        };
        OutputWriter.WriteJson(Path.Combine(_outDir, "sources.json"), sources);
    }
}

public sealed class CountyBallotCsvRow
{
    public string State { get; set; } = "";
    public string County { get; set; } = "";
    public string? OcdDivisionId { get; set; }
    public string ElectionDate { get; set; } = "";
    public string ElectionType { get; set; } = "";
    public string EntryType { get; set; } = "";
    public string? Office { get; set; }
    public string? District { get; set; }
    public string? CandidateName { get; set; }
    public string? Party { get; set; }
    public string? MeasureId { get; set; }
    public string? Title { get; set; }
    public string SourceUrl { get; set; } = "";
}
