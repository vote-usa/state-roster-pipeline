using StateBallot.Core;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace StateBallot.States.Ca;

/// <summary>
/// Parses California's "Official Certified List of Candidates" PDFs (used for
/// both statewide elections and special vacancy elections). Candidate pages
/// have a four-line header, then an office heading, then repeating pairs of
/// "Ballot Name[*] Party" and a ballot-designation line:
///
///   Official Certified List of Candidates
///   Statewide Direct Primary Election - June 2, 2026
///   3/26/2026
///   Page 1 of 53
///   Governor
///   Akinyemi Agbede Democratic
///   Mathematician
///   ...
/// </summary>
public sealed class CertifiedListPdfParser
{
    /// <summary>Extracts candidate rows; election fields and source URL are stamped by the caller.</summary>
    public static List<CandidateRow> Parse(byte[] pdfBytes, string sourceUrl)
    {
        var candidates = new List<CandidateRow>();
        using var pdf = PdfDocument.Open(pdfBytes);

        foreach (var page in pdf.GetPages())
        {
            var lines = ContentOrderTextExtractor.GetText(page)
                .Split('\n')
                .Select(l => l.Replace('\u00a0', ' ').Trim())
                .Where(l => l.Length > 0)
                .ToList();

            // Only candidate pages carry the list header; the first page is the
            // Secretary of State's certification letter.
            if (!lines.Any(l => l.StartsWith("Official Certified List of Candidates", StringComparison.OrdinalIgnoreCase)))
                continue;

            string? office = null;
            var expectDesignation = false;
            foreach (var line in lines)
            {
                if (CaSelectors.CertListSkipLine.IsMatch(line) || CaSelectors.CertListElectionLine.IsMatch(line))
                    continue;

                if (expectDesignation)
                {
                    // Ballot designation line ("Mathematician", "No Ballot Designation", ...); not part of the schema.
                    expectDesignation = false;
                    continue;
                }

                var match = CaSelectors.CertListCandidateLine.Match(line);
                if (match.Success && office is not null)
                {
                    var party = match.Groups["party"].Value;
                    candidates.Add(new CandidateRow
                    {
                        Office = OfficePart(office),
                        District = DistrictPart(office),
                        County = null,
                        CandidateName = match.Groups["name"].Value.Trim(),
                        Party = party == "Unknown" ? null : party,
                        Incumbent = match.Groups["incumbent"].Success ? true : null,
                        SourceUrl = sourceUrl,
                    });
                    expectDesignation = true;
                }
                else
                {
                    // Any non-candidate line at this point is an office heading
                    // (offices repeat in the page header when a list spans pages).
                    office = line;
                }
            }
        }

        if (candidates.Count == 0)
            throw new InvalidOperationException(
                $"No candidates parsed from certified list PDF at {sourceUrl} " +
                $"(candidate pattern '{CaSelectors.CertListCandidateLine}'). The PDF layout may have changed.");

        return candidates;
    }

    private static string OfficePart(string office)
    {
        var match = CaSelectors.OfficeWithDistrict.Match(office);
        return match.Success ? match.Groups["office"].Value.Trim() : office;
    }

    private static string? DistrictPart(string office)
    {
        var match = CaSelectors.OfficeWithDistrict.Match(office);
        return match.Success ? match.Groups["district"].Value : null;
    }
}
