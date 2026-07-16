using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using CsvHelper;
using CsvHelper.Configuration;

namespace StateBallot.Core;

/// <summary>
/// Writes deterministic JSON (snake_case keys, indented) and CSV outputs.
/// Callers are expected to pass pre-sorted rows so re-runs are idempotent.
/// </summary>
public static class OutputWriter
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
        var config = new CsvConfiguration(CultureInfo.InvariantCulture);
        using var writer = new StreamWriter(path);
        using var csv = new CsvWriter(writer, config);
        csv.WriteRecords(rows);
    }
}
