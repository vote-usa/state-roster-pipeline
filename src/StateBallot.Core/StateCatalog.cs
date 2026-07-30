using System.Text.Json;
using System.Text.Json.Serialization;

namespace StateBallot.Core;

/// <summary>One entry in data/state_catalog.json.</summary>
public sealed class StateCatalogEntry
{
    public string Name { get; set; } = "";
    public string Fips { get; set; } = "";

    /// <summary>"implemented" or "unimplemented".</summary>
    public string Status { get; set; } = "unimplemented";

    /// <summary>Assembly/project name when implemented, e.g. StateBallot.States.Ca.</summary>
    public string? Project { get; set; }
}

/// <summary>Loads and queries the national state catalog (50 states + DC).</summary>
public sealed class StateCatalog
{
    private readonly Dictionary<string, StateCatalogEntry> _entries;

    public StateCatalog(IReadOnlyDictionary<string, StateCatalogEntry> entries)
    {
        _entries = new Dictionary<string, StateCatalogEntry>(entries, StringComparer.OrdinalIgnoreCase);
    }

    public static StateCatalog Load(string path)
    {
        if (!File.Exists(path))
            throw new InvalidOperationException($"State catalog not found at {path}.");

        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        };
        var raw = JsonSerializer.Deserialize<Dictionary<string, StateCatalogEntry>>(
                      File.ReadAllText(path), options)
                  ?? throw new InvalidOperationException($"State catalog at {path} is empty.");

        if (raw.Count < 50)
            throw new InvalidOperationException(
                $"State catalog at {path} has {raw.Count} entries; expected at least 50 states + DC.");

        return new StateCatalog(raw);
    }

    /// <summary>Walks up from a start directory to find data/state_catalog.json.</summary>
    public static StateCatalog LoadFromDataRoot(string dataRoot) =>
        Load(Path.Combine(dataRoot, "state_catalog.json"));

    public bool TryGet(string stateCode, out StateCatalogEntry entry) =>
        _entries.TryGetValue(stateCode, out entry!);

    public StateCatalogEntry Get(string stateCode) =>
        TryGet(stateCode, out var entry)
            ? entry
            : throw new InvalidOperationException(
                $"State '{stateCode}' is not in the catalog. Known codes: {string.Join(", ", Codes.Order())}.");

    public IEnumerable<string> Codes => _entries.Keys;

    public IEnumerable<string> ImplementedCodes =>
        _entries.Where(kv => IsImplemented(kv.Value.Status)).Select(kv => kv.Key.ToUpperInvariant());

    public static bool IsImplemented(string status) =>
        string.Equals(status, "implemented", StringComparison.OrdinalIgnoreCase);
}

/// <summary>Marks an IStateCollector with its two-letter state code for assembly discovery.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class StateCodeAttribute : Attribute
{
    public string Code { get; }

    public StateCodeAttribute(string code) => Code = code.ToUpperInvariant();
}
