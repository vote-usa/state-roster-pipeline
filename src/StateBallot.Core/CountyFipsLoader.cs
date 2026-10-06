using System.Text.Json;

namespace StateBallot.Core;

/// <summary>Loads a state's county-name to FIPS-code JSON file.</summary>
public static class CountyFipsLoader
{
    /// <summary>
    /// Loads county-name -> FIPS-code JSON, throwing if the file is missing or
    /// deserializes empty. For states where the FIPS file is the authoritative
    /// source of the expected county set.
    /// </summary>
    public static Dictionary<string, string> LoadRequired(string fipsFilePath)
    {
        if (!File.Exists(fipsFilePath))
            throw new InvalidOperationException($"County FIPS data file not found at {fipsFilePath}.");
        return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(fipsFilePath))
               ?? throw new InvalidOperationException($"County FIPS data file {fipsFilePath} is empty.");
    }

    /// <summary>
    /// Loads county-name -> FIPS-code JSON, or an empty dictionary if the file is
    /// missing or empty. For states where FIPS is optional row enrichment.
    /// </summary>
    public static Dictionary<string, string> LoadOrEmpty(string fipsFilePath) =>
        File.Exists(fipsFilePath)
            ? JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(fipsFilePath)) ?? new()
            : new();
}
