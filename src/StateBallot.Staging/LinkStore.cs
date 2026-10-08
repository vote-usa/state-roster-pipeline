using Dapper;
using MySqlConnector;
using StateBallot.Core;

namespace StateBallot.Staging;

/// <summary>
/// Source links and parameters in roster_staging (SourceLinks, SourceParameters), and their
/// round trip with the seed files at data/input/&lt;xx&gt;/source_links.json.
/// <list type="bullet">
/// <item><see cref="SeedAsync"/> runs after every migration: it inserts rows the database is
/// missing and never overwrites one, so console edits survive. Rows that differ from the seed,
/// or that the seed doesn't have, are reported.</item>
/// <item><see cref="ExportAsync"/> writes the database back to the seed files, so an edit worth
/// keeping can be committed before a dev database is wiped.</item>
/// <item><see cref="ReseedAsync"/> replaces a state's rows with the seed file: git wins.</item>
/// </list>
/// </summary>
public sealed class LinkStore
{
    public const string SeedUser = "seed";

    private readonly string _connectionString;

    public LinkStore(string stagingConnectionString) => _connectionString = stagingConnectionString;

    public sealed record SeedReport(int Inserted, int Unchanged, IReadOnlyList<string> Notes);

    /// <summary>The state's links from the database, or null when it has none.</summary>
    public async Task<SourceLinkSet?> LoadAsync(string stateCode, CancellationToken ct = default)
    {
        await using var cn = await OpenAsync(ct);
        var state = stateCode.ToUpperInvariant();
        var links = (await cn.QueryAsync<LinkRow>(new CommandDefinition(
            "SELECT * FROM SourceLinks WHERE StateCode = @state ORDER BY SourceLinkId", new { state }, cancellationToken: ct))).ToList();
        if (links.Count == 0)
            return null;
        var parameters = await cn.QueryAsync<ParameterRow>(new CommandDefinition(
            "SELECT * FROM SourceParameters WHERE StateCode = @state ORDER BY SourceParameterId", new { state }, cancellationToken: ct));

        var builder = new MySqlConnectionStringBuilder(_connectionString);
        return new SourceLinkSet(
            state, links.Select(r => r.ToLink()), parameters.Select(r => r.ToParameter()),
            $"{builder.Database}.SourceLinks");
    }

    /// <summary>Inserts every seed row the database is missing. Existing rows are kept as they are.</summary>
    public async Task<SeedReport> SeedAsync(string dataRoot, CancellationToken ct = default)
    {
        var seeds = LoadSeeds(dataRoot, null);
        var inserted = 0;
        var unchanged = 0;
        var notes = new List<string>();

        await using var cn = await OpenAsync(ct);
        await using var tx = await cn.BeginTransactionAsync(ct);
        foreach (var seed in seeds)
        {
            var state = seed.StateCode;
            var dbLinks = (await cn.QueryAsync<LinkRow>(new CommandDefinition(
                "SELECT * FROM SourceLinks WHERE StateCode = @state", new { state }, tx, cancellationToken: ct)))
                .ToDictionary(r => Id(r.LinkKey, r.Variant), StringComparer.OrdinalIgnoreCase);
            var dbParams = (await cn.QueryAsync<ParameterRow>(new CommandDefinition(
                "SELECT * FROM SourceParameters WHERE StateCode = @state", new { state }, tx, cancellationToken: ct)))
                .ToDictionary(r => Id(r.ParamKey, r.Variant), StringComparer.OrdinalIgnoreCase);

            foreach (var link in seed.Links)
            {
                var id = Id(link.Key, link.VariantKey);
                if (dbLinks.Remove(id, out var row))
                {
                    if (Same(row.ToLink(), link))
                        unchanged++;
                    else
                        notes.Add($"{state} link {SourceLinkSet.Describe(link.Key, link.VariantKey)} differs from the seed " +
                                  $"(edited by {row.UpdatedBy} {row.UpdatedAt:yyyy-MM-dd}); kept the database value.");
                    continue;
                }
                await InsertLinkAsync(cn, tx, state, link, ct);
                inserted++;
            }
            foreach (var p in seed.Parameters)
            {
                var id = Id(p.Key, p.VariantKey);
                if (dbParams.Remove(id, out var row))
                {
                    if (Same(row.ToParameter(), p))
                        unchanged++;
                    else
                        notes.Add($"{state} parameter {SourceLinkSet.Describe(p.Key, p.VariantKey)} differs from the seed " +
                                  $"(edited by {row.UpdatedBy} {row.UpdatedAt:yyyy-MM-dd}); kept the database value.");
                    continue;
                }
                await InsertParameterAsync(cn, tx, state, p, ct);
                inserted++;
            }

            foreach (var row in dbLinks.Values)
                notes.Add($"{state} link {SourceLinkSet.Describe(row.LinkKey, row.Variant)} is only in the database.");
            foreach (var row in dbParams.Values)
                notes.Add($"{state} parameter {SourceLinkSet.Describe(row.ParamKey, row.Variant)} is only in the database.");
        }
        await tx.CommitAsync(ct);
        return new SeedReport(inserted, unchanged, notes);
    }

    /// <summary>
    /// Writes each state's database rows to its seed file, in insertion order. Returns the
    /// states written. A state with no rows in the database is skipped, not emptied.
    /// </summary>
    public async Task<IReadOnlyList<string>> ExportAsync(string dataRoot, string? stateCode, CancellationToken ct = default)
    {
        var states = stateCode is null
            ? LoadSeeds(dataRoot, null).Select(s => s.StateCode).ToList()
            : [stateCode.ToUpperInvariant()];
        var written = new List<string>();
        foreach (var state in states)
        {
            var set = await LoadAsync(state, ct);
            if (set is null)
                continue;
            set.WriteFile(DataPaths.SourceLinksPath(dataRoot, state));
            written.Add(state);
        }
        return written;
    }

    /// <summary>Replaces the state's rows (or every seeded state's) with its seed file. Rows only in the database are deleted.</summary>
    public async Task<IReadOnlyList<string>> ReseedAsync(string dataRoot, string? stateCode, CancellationToken ct = default)
    {
        var seeds = LoadSeeds(dataRoot, stateCode);
        await using var cn = await OpenAsync(ct);
        await using var tx = await cn.BeginTransactionAsync(ct);
        foreach (var seed in seeds)
        {
            var state = seed.StateCode;
            await cn.ExecuteAsync(new CommandDefinition("DELETE FROM SourceLinks WHERE StateCode = @state", new { state }, tx, cancellationToken: ct));
            await cn.ExecuteAsync(new CommandDefinition("DELETE FROM SourceParameters WHERE StateCode = @state", new { state }, tx, cancellationToken: ct));
            foreach (var link in seed.Links)
                await InsertLinkAsync(cn, tx, state, link, ct);
            foreach (var p in seed.Parameters)
                await InsertParameterAsync(cn, tx, state, p, ct);
        }
        await tx.CommitAsync(ct);
        return seeds.Select(s => s.StateCode).ToList();
    }

    /// <summary>Every data/input/&lt;xx&gt;/source_links.json, or just one state's.</summary>
    public static List<SourceLinkSet> LoadSeeds(string dataRoot, string? stateCode)
    {
        if (stateCode is not null)
            return [SourceLinkSet.Load(dataRoot, stateCode)];

        var inputRoot = DataPaths.InputRoot(dataRoot);
        if (!Directory.Exists(inputRoot))
            return [];
        return Directory.EnumerateDirectories(inputRoot)
            .Where(d => File.Exists(Path.Combine(d, SourceLinkSet.FileName)))
            .Order(StringComparer.Ordinal)
            .Select(d => SourceLinkSet.LoadFile(Path.Combine(d, SourceLinkSet.FileName), Path.GetFileName(d)))
            .ToList();
    }

    private async Task<MySqlConnection> OpenAsync(CancellationToken ct)
    {
        var cn = new MySqlConnection(_connectionString);
        await cn.OpenAsync(ct);
        return cn;
    }

    private static Task InsertLinkAsync(MySqlConnection cn, MySqlTransaction tx, string state, SourceLink link, CancellationToken ct) =>
        cn.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO SourceLinks (StateCode, LinkKey, Variant, UrlTemplate, Format, Kind, Notes, IsActive, UpdatedAt, UpdatedBy)
            VALUES (@state, @Key, @Variant, @Url, @Format, @Kind, @Notes, @IsActive, UTC_TIMESTAMP(), @by)
            """,
            new
            {
                state, link.Key, Variant = link.VariantKey, link.Url, link.Format, Kind = link.KindOrFetch,
                link.Notes, link.IsActive, by = SeedUser,
            },
            tx, cancellationToken: ct));

    private static Task InsertParameterAsync(MySqlConnection cn, MySqlTransaction tx, string state, SourceParameter p, CancellationToken ct) =>
        cn.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO SourceParameters (StateCode, ParamKey, Variant, Value, Notes, UpdatedAt, UpdatedBy)
            VALUES (@state, @Key, @Variant, @Value, @Notes, UTC_TIMESTAMP(), @by)
            """,
            new { state, p.Key, Variant = p.VariantKey, p.Value, p.Notes, by = SeedUser },
            tx, cancellationToken: ct));

    private static string Id(string key, string variant) => $"{key}\u001f{variant}";

    private static bool Same(SourceLink a, SourceLink b) =>
        a.Url == b.Url && a.Format == b.Format && a.KindOrFetch == b.KindOrFetch && a.Notes == b.Notes && a.IsActive == b.IsActive;

    private static bool Same(SourceParameter a, SourceParameter b) => a.Value == b.Value && a.Notes == b.Notes;

    private sealed class LinkRow
    {
        public string LinkKey { get; set; } = "";
        public string Variant { get; set; } = "";
        public string UrlTemplate { get; set; } = "";
        public string? Format { get; set; }
        public string Kind { get; set; } = SourceLinkKind.Fetch;
        public string? Notes { get; set; }
        public bool IsActive { get; set; }
        public DateTime UpdatedAt { get; set; }
        public string UpdatedBy { get; set; } = "";

        // Normalized the same way a seed file is, so comparisons are like for like.
        public SourceLink ToLink() => SourceLinkSet.Normalize(new SourceLink
        {
            Key = LinkKey, Variant = Variant, Url = UrlTemplate, Format = Format, Kind = Kind, Notes = Notes,
            Active = IsActive,
        });
    }

    private sealed class ParameterRow
    {
        public string ParamKey { get; set; } = "";
        public string Variant { get; set; } = "";
        public string Value { get; set; } = "";
        public string? Notes { get; set; }
        public DateTime UpdatedAt { get; set; }
        public string UpdatedBy { get; set; } = "";

        public SourceParameter ToParameter() => SourceLinkSet.Normalize(new SourceParameter
        {
            Key = ParamKey, Variant = Variant, Value = Value, Notes = Notes,
        });
    }
}
