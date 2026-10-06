using System.Text.Json;

namespace StateBallot.Core;

/// <summary>
/// Loads a state's raw-code-to-canonical-name lookup table (e.g. TX's election
/// type codes) from a JSON object file. Unlike DateFormatConfig, an empty table
/// is valid - it just means every value passes through unchanged - so only a
/// missing or malformed file is an error, never a miss on an individual key.
/// </summary>
public static class LookupTableLoader
{
    public static Dictionary<string, string> Load(string path)
    {
        if (!File.Exists(path))
            throw new InvalidOperationException($"Lookup table config file not found at {path}.");
        return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path))
               ?? throw new InvalidOperationException($"Lookup table config at {path} is malformed (not a JSON object).");
    }
}
