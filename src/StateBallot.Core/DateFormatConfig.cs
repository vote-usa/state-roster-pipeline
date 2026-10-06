using System.Text.Json;

namespace StateBallot.Core;

/// <summary>
/// Loads a state's accepted date-parsing formats from data/input/&lt;xx&gt;/date_formats.json
/// (a bare JSON array of .NET custom date format strings, e.g. ["yyyy-MM-dd"]).
/// </summary>
public static class DateFormatConfig
{
    /// <summary>
    /// Loads the format list, throwing if the file is missing or empty. Every
    /// election date parsed by a collector feeds year filtering, sort order,
    /// publish-schedule arithmetic, and the shipped output rows - an empty or
    /// missing format list must fail loudly here rather than let every
    /// downstream DateParsing.TryParseAny call quietly reject everything.
    /// </summary>
    public static string[] Load(string path)
    {
        if (!File.Exists(path))
            throw new InvalidOperationException($"Date format config file not found at {path}.");
        var formats = JsonSerializer.Deserialize<string[]>(File.ReadAllText(path));
        return formats is { Length: > 0 }
            ? formats
            : throw new InvalidOperationException($"Date format config at {path} is empty.");
    }
}
