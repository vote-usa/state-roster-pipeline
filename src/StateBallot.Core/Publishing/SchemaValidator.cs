using System.Text.Json;
using Json.Schema;

namespace StateBallot.Core.Publishing;

/// <summary>One validation failure: which file, where in it, and why.</summary>
public sealed record SchemaError(string File, string Location, string Message)
{
    public override string ToString() => $"{File} {Location}: {Message}";
}

/// <summary>
/// Validates roster output files against the JSON Schemas in schema/. The schema for a
/// file is picked by file name (candidates.json -> candidates.schema.json). Files with
/// no matching schema are skipped, not failed, so unrelated JSON can sit next to outputs.
/// </summary>
public sealed class SchemaValidator
{
    public const string SchemaDirEnvVar = "ROSTER_SCHEMA_DIR";

    // JsonSchema.Net resolves cross-file $refs through a process-global registry. Load and
    // register each schema directory once so concurrent validators never re-register
    // schemas another thread is evaluating with.
    private static readonly object LoadLock = new();
    private static readonly Dictionary<string, Dictionary<string, JsonSchema>> Loaded =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<string, JsonSchema> _byFileName;
    private readonly EvaluationOptions _options = new() { OutputFormat = OutputFormat.Hierarchical };

    public string SchemaDir { get; }

    public SchemaValidator(string? schemaDir = null)
    {
        SchemaDir = Path.GetFullPath(schemaDir ?? FindSchemaDir());
        if (!Directory.Exists(SchemaDir))
            throw new InvalidOperationException($"Schema directory not found: {SchemaDir}");

        lock (LoadLock)
        {
            if (!Loaded.TryGetValue(SchemaDir, out var byFileName))
            {
                byFileName = new Dictionary<string, JsonSchema>(StringComparer.OrdinalIgnoreCase);
                foreach (var path in Directory.EnumerateFiles(SchemaDir, "*.schema.json"))
                {
                    var schema = JsonSchema.FromFile(path);
                    SchemaRegistry.Global.Register(schema);
                    var target = Path.GetFileName(path).Replace(".schema.json", ".json", StringComparison.OrdinalIgnoreCase);
                    byFileName[target] = schema;
                }

                if (byFileName.Count == 0)
                    throw new InvalidOperationException($"No *.schema.json files in {SchemaDir}");
                Loaded[SchemaDir] = byFileName;
            }

            _byFileName = byFileName;
        }
    }

    /// <summary>File names this validator knows a schema for (e.g. candidates.json).</summary>
    public IReadOnlyCollection<string> KnownFileNames => _byFileName.Keys;

    public bool HasSchemaFor(string filePath) => _byFileName.ContainsKey(Path.GetFileName(filePath));

    /// <summary>Validates one file. Empty list = valid. Unknown file name = empty list (skipped).</summary>
    public IReadOnlyList<SchemaError> ValidateFile(string filePath)
    {
        if (!_byFileName.TryGetValue(Path.GetFileName(filePath), out var schema))
            return Array.Empty<SchemaError>();

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(File.ReadAllText(filePath));
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return [new SchemaError(filePath, "", $"not valid JSON: {ex.Message}")];
        }

        using (doc)
            return ValidateElement(doc.RootElement, schema, filePath);
    }

    /// <summary>Validates every known file under a directory (recursively).</summary>
    public IReadOnlyList<SchemaError> ValidateDirectory(string dir)
    {
        var errors = new List<SchemaError>();
        foreach (var path in Directory.EnumerateFiles(dir, "*.json", SearchOption.AllDirectories)
                     .Where(HasSchemaFor)
                     .OrderBy(p => p, StringComparer.Ordinal))
            errors.AddRange(ValidateFile(path));
        return errors;
    }

    /// <summary>Validates an in-memory document as if it were the named file.</summary>
    public IReadOnlyList<SchemaError> ValidateJson(string fileName, string json)
    {
        if (!_byFileName.TryGetValue(fileName, out var schema))
            throw new ArgumentException($"No schema for {fileName}", nameof(fileName));
        using var doc = JsonDocument.Parse(json);
        return ValidateElement(doc.RootElement, schema, fileName);
    }

    private IReadOnlyList<SchemaError> ValidateElement(JsonElement element, JsonSchema schema, string file)
    {
        var results = schema.Evaluate(element, _options);
        if (results.IsValid)
            return Array.Empty<SchemaError>();

        var errors = new List<SchemaError>();
        Collect(results, file, errors);
        if (errors.Count == 0)
            errors.Add(new SchemaError(file, "", "does not match schema (no detail available)"));
        return errors;
    }

    // Applicator keywords whose own message only summarizes their children
    // ("Some items do not match ... failing indexes: [...]"). The children carry the
    // specific error, so the summary is dropped whenever an invalid child exists.
    private static readonly HashSet<string> AggregateKeywords =
        new(StringComparer.Ordinal) { "items", "prefixItems", "properties", "anyOf", "oneOf", "allOf" };

    /// <summary>
    /// Walks the hierarchical result. Only invalid nodes are descended into, so the failed
    /// alternatives of a passing anyOf (e.g. nullable fields) are not reported as errors.
    /// </summary>
    private static void Collect(EvaluationResults node, string file, List<SchemaError> errors)
    {
        if (node.IsValid)
            return;

        var invalidChildren = node.Details?.Where(d => !d.IsValid).ToList() ?? [];

        if (node.Errors is { Count: > 0 })
        {
            foreach (var (keyword, message) in node.Errors)
            {
                if (invalidChildren.Count > 0 && AggregateKeywords.Contains(keyword))
                    continue;
                errors.Add(new SchemaError(file, node.InstanceLocation.ToString(), $"{keyword}: {message}"));
            }
        }

        foreach (var child in invalidChildren)
            Collect(child, file, errors);
    }

    /// <summary>
    /// ROSTER_SCHEMA_DIR, else schema/ beside the executable (copied at build), else the
    /// repo's schema/ walking up from the executable.
    /// </summary>
    public static string FindSchemaDir()
    {
        var fromEnv = Environment.GetEnvironmentVariable(SchemaDirEnvVar);
        if (!string.IsNullOrWhiteSpace(fromEnv))
            return Path.GetFullPath(fromEnv);

        var beside = Path.Combine(AppContext.BaseDirectory, "schema");
        if (Directory.Exists(beside))
            return beside;

        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
        {
            var candidate = Path.Combine(d.FullName, "schema");
            if (Directory.Exists(candidate) && Directory.Exists(Path.Combine(d.FullName, "src")))
                return candidate;
        }

        return Path.Combine(Environment.CurrentDirectory, "schema");
    }
}

/// <summary>Thrown when files the pipeline just wrote do not satisfy the output contract.</summary>
public sealed class SchemaValidationException(IReadOnlyList<SchemaError> errors)
    : Exception(BuildMessage(errors))
{
    public IReadOnlyList<SchemaError> Errors { get; } = errors;

    private static string BuildMessage(IReadOnlyList<SchemaError> errors)
    {
        var shown = errors.Take(5).Select(e => "  " + e);
        var more = errors.Count > 5 ? $"\n  ... and {errors.Count - 5} more" : "";
        return $"Output failed schema validation ({errors.Count} error(s)):\n{string.Join("\n", shown)}{more}";
    }
}
