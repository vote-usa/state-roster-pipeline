using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace StateBallot.Core;

public static class SourceLinkKind
{
    /// <summary>A URL the collector fetches.</summary>
    public const string Fetch = "fetch";

    /// <summary>The page a person checks by hand when a run comes back empty.</summary>
    public const string Home = "home";

    /// <summary>Listed in the manifest's verification_only for cross-checking by hand, never fetched.</summary>
    public const string Verification = "verification";

    public static readonly IReadOnlySet<string> All = new HashSet<string> { Fetch, Home, Verification };
}

/// <summary>
/// One configured source URL for a state. <see cref="Key"/> matches the fetch role the
/// collector tags the download with, so a link joins to the CaptureFetches rows it produced.
/// <see cref="Url"/> may hold {year}, {electionId}, {countyCode} or {raceId}.
/// Optional fields are null when they hold their default, which keeps seed files short.
/// </summary>
public sealed class SourceLink
{
    public string Key { get; set; } = "";

    /// <summary>Lower-case variant such as "primary" or "general"; null when the key has one URL.</summary>
    public string? Variant { get; set; }

    public string Url { get; set; } = "";
    public string? Format { get; set; }

    /// <summary>Null means <see cref="SourceLinkKind.Fetch"/>.</summary>
    public string? Kind { get; set; }

    public string? Notes { get; set; }

    /// <summary>Null means active.</summary>
    public bool? Active { get; set; }

    [JsonIgnore] public string VariantKey => Variant ?? "";
    [JsonIgnore] public string KindOrFetch => Kind ?? SourceLinkKind.Fetch;
    [JsonIgnore] public bool IsActive => Active ?? true;
}

/// <summary>
/// A hand-maintained value a collector needs besides a URL, such as an election id the
/// source has no index for. Edited each cycle, so it lives beside the links.
/// </summary>
public sealed class SourceParameter
{
    public string Key { get; set; } = "";
    public string? Variant { get; set; }
    public string Value { get; set; } = "";
    public string? Notes { get; set; }

    [JsonIgnore] public string VariantKey => Variant ?? "";
}

/// <summary>The shape of data/input/&lt;xx&gt;/source_links.json, and of a capture's links snapshot.</summary>
public sealed class SourceLinkFile
{
    public string State { get; set; } = "";
    public List<SourceLink> Links { get; set; } = [];
    public List<SourceParameter> Parameters { get; set; } = [];
}

/// <summary>
/// One state's source links and parameters, loaded from the seed file or the staging
/// database. Validated on construction: duplicate (key, variant) pairs, unknown kinds and
/// unknown placeholders fail immediately rather than at fetch time.
/// </summary>
public sealed partial class SourceLinkSet
{
    public const string FileName = "source_links.json";

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private static readonly HashSet<string> Placeholders = ["year", "electionId", "countyCode", "raceId"];

    private readonly Dictionary<(string Key, string Variant), SourceLink> _links;
    private readonly Dictionary<(string Key, string Variant), SourceParameter> _parameters;

    public SourceLinkSet(string stateCode, IEnumerable<SourceLink> links, IEnumerable<SourceParameter> parameters, string origin)
    {
        StateCode = stateCode.ToUpperInvariant();
        Origin = origin;
        Links = links.Select(Normalize).ToList();
        Parameters = parameters.Select(Normalize).ToList();

        _links = new(Comparer);
        foreach (var link in Links)
        {
            if (string.IsNullOrWhiteSpace(link.Key) || string.IsNullOrWhiteSpace(link.Url))
                throw new InvalidOperationException($"{origin}: every link needs a key and a url.");
            if (!SourceLinkKind.All.Contains(link.KindOrFetch))
                throw new InvalidOperationException($"{origin}: link {Describe(link.Key, link.VariantKey)} has unknown kind '{link.Kind}'.");
            foreach (Match m in PlaceholderRegex().Matches(link.Url))
            {
                if (!Placeholders.Contains(m.Groups[1].Value))
                    throw new InvalidOperationException(
                        $"{origin}: link {Describe(link.Key, link.VariantKey)} uses unknown placeholder {m.Value}. " +
                        $"Known: {string.Join(", ", Placeholders.Select(p => $"{{{p}}}"))}.");
            }
            if (!_links.TryAdd((link.Key, link.VariantKey), link))
                throw new InvalidOperationException($"{origin}: link {Describe(link.Key, link.VariantKey)} is listed twice.");
        }

        _parameters = new(Comparer);
        foreach (var parameter in Parameters)
        {
            if (!_parameters.TryAdd((parameter.Key, parameter.VariantKey), parameter))
                throw new InvalidOperationException($"{origin}: parameter {Describe(parameter.Key, parameter.VariantKey)} is listed twice.");
        }
    }

    public string StateCode { get; }

    /// <summary>Where the set came from, for logs: a file path or a database name.</summary>
    public string Origin { get; }

    public IReadOnlyList<SourceLink> Links { get; }
    public IReadOnlyList<SourceParameter> Parameters { get; }

    /// <summary>Loads data/input/&lt;xx&gt;/source_links.json.</summary>
    public static SourceLinkSet Load(string inputDataRoot, string stateCode) =>
        LoadFile(DataPaths.SourceLinksPath(inputDataRoot, stateCode), stateCode);

    public static SourceLinkSet LoadFile(string path, string? expectedState = null)
    {
        if (!File.Exists(path))
            throw new InvalidOperationException($"No source links at {path}.");
        var file = JsonSerializer.Deserialize<SourceLinkFile>(File.ReadAllText(path), JsonOptions)
            ?? throw new InvalidOperationException($"{path} is empty.");
        return FromFile(file, path, expectedState);
    }

    public static SourceLinkSet FromFile(SourceLinkFile file, string origin, string? expectedState = null)
    {
        if (expectedState is not null && !string.Equals(file.State, expectedState, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"{origin} is for state '{file.State}', expected {expectedState.ToUpperInvariant()}.");
        return new SourceLinkSet(file.State, file.Links, file.Parameters, origin);
    }

    public SourceLinkFile ToFile() => new()
    {
        State = StateCode,
        Links = [.. Links],
        Parameters = [.. Parameters],
    };

    public string ToJson() => JsonSerializer.Serialize(ToFile(), JsonOptions) + "\n";

    public void WriteFile(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, ToJson());
    }

    /// <summary>
    /// The active link for <paramref name="key"/> and <paramref name="variant"/> with its
    /// placeholders filled. Throws when the link is missing or inactive, or when the
    /// template needs a value that was not supplied. Unused values are ignored.
    /// </summary>
    public string Url(
        string key, string? variant = null, int? year = null, string? electionId = null,
        string? countyCode = null, string? raceId = null)
    {
        var variantKey = variant ?? "";
        if (!_links.TryGetValue((key, variantKey), out var link) || !link.IsActive)
            throw new InvalidOperationException(
                $"No active {StateCode} source link {Describe(key, variantKey)} in {Origin}.");

        return PlaceholderRegex().Replace(link.Url, m =>
        {
            var value = m.Groups[1].Value switch
            {
                "year" => year?.ToString(System.Globalization.CultureInfo.InvariantCulture),
                "electionId" => electionId,
                "countyCode" => countyCode,
                "raceId" => raceId,
                _ => null,
            };
            return value ?? throw new InvalidOperationException(
                $"{StateCode} source link {Describe(key, variantKey)} needs {m.Value}, which was not supplied.");
        });
    }

    /// <summary>Active links of one kind, in seed-file order.</summary>
    public IEnumerable<SourceLink> OfKind(string kind) =>
        Links.Where(l => l.IsActive && l.KindOrFetch == kind);

    /// <summary>The parameter's value. Throws when it is missing or blank.</summary>
    public string Parameter(string key, string? variant = null) =>
        FindParameter(key, variant) ?? throw new InvalidOperationException(
            $"No {StateCode} source parameter {Describe(key, variant ?? "")} in {Origin}.");

    /// <summary>The parameter's value, or null when it is missing or blank.</summary>
    public string? FindParameter(string key, string? variant = null) =>
        _parameters.TryGetValue((key, variant ?? ""), out var p) && !string.IsNullOrWhiteSpace(p.Value) ? p.Value : null;

    public static string Describe(string key, string variant) => variant.Length == 0 ? key : $"{key}/{variant}";

    private static readonly IEqualityComparer<(string, string)> Comparer = new KeyComparer();

    /// <summary>A copy with blanks as null, the variant lower-cased, and default kind/active as null, as stored in seed files.</summary>
    public static SourceLink Normalize(SourceLink l) => new()
    {
        Key = l.Key.Trim(),
        Variant = Blank(l.Variant)?.ToLowerInvariant(),
        Url = l.Url.Trim(),
        Format = Blank(l.Format),
        Kind = Blank(l.Kind) is { } k && k != SourceLinkKind.Fetch ? k : null,
        Notes = Blank(l.Notes),
        Active = l.Active is false ? false : null,
    };

    /// <summary>A copy with blanks as null and the variant lower-cased, as stored in seed files.</summary>
    public static SourceParameter Normalize(SourceParameter p) => new()
    {
        Key = p.Key.Trim(),
        Variant = Blank(p.Variant)?.ToLowerInvariant(),
        Value = p.Value.Trim(),
        Notes = Blank(p.Notes),
    };

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    [GeneratedRegex(@"\{(\w+)\}")]
    private static partial Regex PlaceholderRegex();

    private sealed class KeyComparer : IEqualityComparer<(string, string)>
    {
        public bool Equals((string, string) a, (string, string) b) =>
            string.Equals(a.Item1, b.Item1, StringComparison.Ordinal) &&
            string.Equals(a.Item2, b.Item2, StringComparison.OrdinalIgnoreCase);

        public int GetHashCode((string, string) k) =>
            HashCode.Combine(k.Item1, StringComparer.OrdinalIgnoreCase.GetHashCode(k.Item2));
    }
}

/// <summary>
/// Base for each state's XxSourceConfig: the typed, named URLs a collector calls, resolved
/// from the state's <see cref="SourceLinkSet"/> instead of compiled-in strings.
/// </summary>
public abstract class SourceConfigBase
{
    private readonly SourceLinkSet? _links;

    public SourceLinkSet Links
    {
        get => _links ?? throw new InvalidOperationException(
            $"{GetType().Name} has no source links. Build it with Links = SourceLinkSet.Load(...).");
        init => _links = value;
    }

    /// <summary>The state's "home" link: the page to check by hand when a run comes back empty.</summary>
    public string HomeUrl(int year) => Links.Url("home", year: year);

    /// <summary>Every active verification link as manifest entries. Empty when the state has none.</summary>
    public List<SourceEntry> VerificationSources(int year) =>
        Links.OfKind(SourceLinkKind.Verification)
            .Select(l => new SourceEntry(Links.Url(l.Key, l.Variant, year: year), l.Format ?? "html"))
            .ToList();

    /// <summary>"primary" for a primary election type, otherwise "general".</summary>
    protected static string PrimaryOrGeneral(string electionType) =>
        string.Equals(electionType, "primary", StringComparison.OrdinalIgnoreCase) ? "primary" : "general";
}
