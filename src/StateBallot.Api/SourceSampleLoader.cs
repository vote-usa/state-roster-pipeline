using System.Text.Json;
using StateBallot.Core;
using StateBallot.States.Wv;

namespace StateBallot.Api;

public sealed record SourceSample(int RowCount, IReadOnlyList<string> Columns, IReadOnlyList<Dictionary<string, string>> SampleRows);

public interface ISourceLoader
{
    Task<SourceSample> LoadAsync(string state, int year, CancellationToken ct = default);
}

public sealed class SourceSampleLoader : ISourceLoader
{
    private const int SampleRowCount = 8;

    public async Task<SourceSample> LoadAsync(string state, int year, CancellationToken ct = default)
    {
        var code = state.Trim().ToUpperInvariant();
        return code switch
        {
            "WV" => await LoadWestVirginiaAsync(year, ct),
            _ => throw new NotSupportedException(
                $"No source loader is wired for '{code}' yet. WV is available; add a loader for others."),
        };
    }

    private static async Task<SourceSample> LoadWestVirginiaAsync(int year, CancellationToken ct)
    {
        var config = new WvSourceConfig();
        using var fetcher = new HttpFetcher();

        var request = new WestVirginiaCandidateSearchRequest { ElectionYear = year, Page = 0, Size = 1000 };
        var json = await fetcher.PostJsonAsync(config.CandidatesUrl, request);
        var candidates = WvCandidateClient.ParsePage(json);

        var rows = candidates.Select(Flatten).ToList();
        return new SourceSample(rows.Count, UnionColumns(rows), rows.Take(SampleRowCount).ToList());
    }

    private static Dictionary<string, string> Flatten<T>(T record)
    {
        var row = new Dictionary<string, string>(StringComparer.Ordinal);
        using var doc = JsonSerializer.SerializeToDocument(record);
        Walk(doc.RootElement, "", row);
        return row;
    }

    private static void Walk(JsonElement element, string prefix, IDictionary<string, string> into)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                    Walk(property.Value, prefix.Length == 0 ? property.Name : $"{prefix}.{property.Name}", into);
                break;
            case JsonValueKind.Array:
                into[prefix] = element.GetRawText();
                break;
            case JsonValueKind.Null or JsonValueKind.Undefined:
                break;
            default:
                into[prefix] = element.ToString();
                break;
        }
    }

    private static List<string> UnionColumns(IEnumerable<Dictionary<string, string>> rows)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var columns = new List<string>();
        foreach (var row in rows)
            foreach (var key in row.Keys)
                if (seen.Add(key))
                    columns.Add(key);
        return columns;
    }
}
