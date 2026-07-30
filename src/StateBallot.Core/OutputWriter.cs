using System.Globalization;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using CsvHelper;
using CsvHelper.Configuration;

namespace StateBallot.Core;

/// <summary>
/// Writes deterministic JSON (snake_case keys, indented) and CSV outputs
/// (snake_case headers). Callers pass pre-sorted rows so re-runs are idempotent.
/// </summary>
public static partial class OutputWriter
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public static void WriteJson<T>(string path, T rows)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(rows, JsonOptions) + "\n");
    }

    public static void WriteCsv<T>(string path, IEnumerable<T> rows)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var config = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            HasHeaderRecord = true,
        };
        using var writer = new StreamWriter(path);
        using var csv = new CsvWriter(writer, config);
        csv.Context.RegisterClassMap(new SnakeCaseMap<T>());
        csv.WriteRecords(rows);
    }

    private sealed class SnakeCaseMap<T> : ClassMap<T>
    {
        public SnakeCaseMap()
        {
            AutoMap(CultureInfo.InvariantCulture);
            foreach (var memberMap in MemberMaps)
            {
                var name = memberMap.Data.Member?.Name;
                if (name is null) continue;
                memberMap.Data.Names.Clear();
                memberMap.Data.Names.Add(ToSnakeCase(name));
            }
        }
    }

    private static string ToSnakeCase(string name) =>
        PascalBoundary().Replace(name, "$1_$2").ToLowerInvariant();

    [GeneratedRegex("([a-z0-9])([A-Z])")]
    private static partial Regex PascalBoundary();
}
