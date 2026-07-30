using System.Globalization;
using System.Text.RegularExpressions;

namespace StateBallot.Core;

/// <summary>Canonical deterministic ordering for CollectResult lists (all states).</summary>
public static partial class CollectResultSorter
{
    public static void Sort(CollectResult result)
    {
        result.Elections.Sort(CompareElections);
        result.Candidates.Sort(CompareCandidates);
        result.Measures.Sort(CompareMeasures);
        result.StatewideProposedMeasures.Sort(CompareMeasures);
        result.CountyDirectory.Sort((a, b) =>
            string.CompareOrdinal(a.CountyName, b.CountyName));
        result.CountyBallots.Sort((a, b) =>
        {
            var c = string.CompareOrdinal(a.CountyName, b.CountyName);
            return c != 0 ? c : string.CompareOrdinal(a.ElectionDate, b.ElectionDate);
        });

        foreach (var ballot in result.CountyBallots)
        {
            ballot.Candidates.Sort(CompareCandidates);
            ballot.Measures.Sort(CompareMeasures);
        }
    }

    public static int CompareElections(Election a, Election b)
    {
        var c = a.ElectionDate.CompareTo(b.ElectionDate);
        return c != 0 ? c : string.CompareOrdinal(a.ElectionId, b.ElectionId);
    }

    public static int CompareCandidates(CandidateRow a, CandidateRow b)
    {
        var c = string.CompareOrdinal(a.ElectionDate, b.ElectionDate);
        if (c != 0) return c;
        c = string.CompareOrdinal(a.Office, b.Office);
        if (c != 0) return c;
        c = DistrictKey(a.District).CompareTo(DistrictKey(b.District));
        if (c != 0) return c;
        c = string.CompareOrdinal(a.District ?? "", b.District ?? "");
        if (c != 0) return c;
        return string.CompareOrdinal(a.CandidateName, b.CandidateName);
    }

    public static int CompareMeasures(MeasureRow a, MeasureRow b)
    {
        var c = string.CompareOrdinal(a.ElectionDate ?? "", b.ElectionDate ?? "");
        if (c != 0) return c;
        c = string.CompareOrdinal(a.County ?? "", b.County ?? "");
        if (c != 0) return c;
        c = MeasureNumericId(a.MeasureId).CompareTo(MeasureNumericId(b.MeasureId));
        if (c != 0) return c;
        return string.CompareOrdinal(a.MeasureId, b.MeasureId);
    }

    private static int DistrictKey(string? district) =>
        int.TryParse(Digits().Match(district ?? "").Value, NumberStyles.Integer,
            CultureInfo.InvariantCulture, out var n)
            ? n
            : int.MaxValue;

    private static int MeasureNumericId(string measureId)
    {
        var digits = new string(measureId.Where(char.IsDigit).ToArray());
        return int.TryParse(digits, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
            ? n
            : int.MaxValue;
    }

    [GeneratedRegex(@"\d+", RegexOptions.Compiled)]
    private static partial Regex Digits();
}
