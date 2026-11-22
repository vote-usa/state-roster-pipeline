using Dapper;
using MySqlConnector;

namespace StateBallot.Staging;

/// <summary>A named, ordered list of transform steps, run wherever a "ruleset" step links to it.</summary>
public sealed class Ruleset
{
    public int RulesetId { get; set; }
    public string Name { get; set; } = "";
    public string? Description { get; set; }

    /// <summary>A deactivated ruleset still runs wherever it is already linked, but cannot be linked anew.</summary>
    public bool IsActive { get; set; } = true;

    public List<TransformStep> Steps { get; set; } = new();
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public sealed class RulesetSummary
{
    public int RulesetId { get; set; }
    public string Name { get; set; } = "";
    public bool IsActive { get; set; }
}

public sealed class RulesetStore
{
    private const int DuplicateKeyErrorCode = 1062;

    /// <summary>Every (MappingGroupId, RulesetId) a mapping references, at a target or on an input.</summary>
    internal const string MappingReferences =
        """
        SELECT t.MappingGroupId, r.RulesetId
        FROM TargetMappingRulesets r JOIN TargetMappings t ON t.TargetMappingId = r.TargetMappingId
        UNION ALL
        SELECT t.MappingGroupId, r.RulesetId
        FROM MappingInputRulesets r
        JOIN MappingInputs i ON i.MappingInputId = r.MappingInputId
        JOIN TargetMappings t ON t.TargetMappingId = i.TargetMappingId
        """;

    private readonly StagingDb _db;

    public RulesetStore(StagingDb db) => _db = db;

    public async Task<IReadOnlyList<Ruleset>> ListAsync(bool activeOnly = false, string? search = null, CancellationToken ct = default)
    {
        await using var cn = await _db.OpenStagingAsync(ct);
        return await ReadAsync(cn, null, Query.For(activeOnly, search), ct);
    }

    public async Task<IReadOnlyList<RulesetSummary>> ListSummariesAsync(bool activeOnly = false, string? search = null, CancellationToken ct = default)
    {
        await using var cn = await _db.OpenStagingAsync(ct);
        return (await cn.QueryAsync<RulesetSummary>(new CommandDefinition(
            $"SELECT r.RulesetId, r.Name, r.IsActive FROM Rulesets r WHERE {Filter} ORDER BY r.Name",
            Query.For(activeOnly, search), cancellationToken: ct))).ToList();
    }

    /// <summary>Null when there is no such ruleset. Keeps active rulesets linking only to active ones.</summary>
    public async Task<Ruleset?> SetActiveAsync(int rulesetId, bool isActive, CancellationToken ct = default)
    {
        await using var cn = await _db.OpenStagingAsync(ct);
        await using var tx = await cn.BeginTransactionAsync(ct);

        if (!(await LockAllAsync(cn, tx, ct)).Contains(rulesetId))
            return null;

        var byId = await ByIdAsync(cn, tx, ct);
        var ruleset = byId[rulesetId];
        if (!isActive)
        {
            var users = byId.Values
                .Where(r => r.IsActive && r.RulesetId != rulesetId && RulesetSteps.Links(r.Steps).Contains(rulesetId))
                .Select(r => $"'{r.Name}'")
                .ToList();
            if (users.Count > 0)
                throw new ArgumentException(
                    $"Ruleset '{ruleset.Name}' is used by active ruleset {string.Join(", ", users)}. Deactivate them or remove the link first.");
        }
        else
        {
            var inactive = RulesetSteps.Links(ruleset.Steps).Distinct()
                .Where(id => byId.TryGetValue(id, out var linked) && !linked.IsActive)
                .Select(id => $"'{byId[id].Name}'")
                .ToList();
            if (inactive.Count > 0)
                throw new ArgumentException(
                    $"Ruleset '{ruleset.Name}' links to deactivated ruleset {string.Join(", ", inactive)}. Reactivate them first.");
        }

        await cn.ExecuteAsync(new CommandDefinition(
            "UPDATE Rulesets SET IsActive = @IsActive, UpdatedAt = UTC_TIMESTAMP() WHERE RulesetId = @Id",
            new { Id = rulesetId, IsActive = isActive }, transaction: tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
        return (await ReadAsync(cn, null, rulesetId, ct)).Single();
    }

    public async Task<Ruleset?> GetAsync(int rulesetId, CancellationToken ct = default)
    {
        await using var cn = await _db.OpenStagingAsync(ct);
        return (await ReadAsync(cn, null, rulesetId, ct)).SingleOrDefault();
    }

    public async Task<IReadOnlyDictionary<int, Ruleset>> ByIdAsync(CancellationToken ct = default)
    {
        await using var cn = await _db.OpenStagingAsync(ct);
        return await ByIdAsync(cn, null, ct);
    }

    public async Task<Ruleset> CreateAsync(Ruleset ruleset, CancellationToken ct = default)
    {
        var r = Clean(ruleset);
        await using var cn = await _db.OpenStagingAsync(ct);
        await using var tx = await cn.BeginTransactionAsync(ct);

        // Nothing links to a new ruleset yet, so it cannot be in a loop.
        if (RulesetSteps.Links(r.Steps).Any())
        {
            await LockAllAsync(cn, tx, ct);
            var byId = await ByIdAsync(cn, tx, ct);
            RulesetSteps.Expand(r.Steps, byId);
            RulesetSteps.RefuseNewInactiveLinks(RulesetSteps.Links(r.Steps), [], byId.Values);
        }

        var id = await UniqueName(r.Name, () => cn.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            INSERT INTO Rulesets (Name, Description, CreatedAt, UpdatedAt)
            VALUES (@Name, @Description, UTC_TIMESTAMP(), UTC_TIMESTAMP());
            SELECT LAST_INSERT_ID();
            """,
            new { r.Name, r.Description },
            transaction: tx, cancellationToken: ct)));

        await InsertStepsAsync(cn, tx, id, r.Steps, ct);
        await tx.CommitAsync(ct);
        return (await ReadAsync(cn, null, id, ct)).Single();
    }

    /// <summary>Replaces the ruleset's name, description and steps. Null when there is no such ruleset.</summary>
    public async Task<Ruleset?> UpdateAsync(int rulesetId, Ruleset ruleset, CancellationToken ct = default)
    {
        var r = Clean(ruleset);
        await using var cn = await _db.OpenStagingAsync(ct);
        await using var tx = await cn.BeginTransactionAsync(ct);

        if (!(await LockAllAsync(cn, tx, ct)).Contains(rulesetId))
            return null;

        var byId = await ByIdAsync(cn, tx, ct);
        RulesetSteps.Expand(r.Steps, byId, rulesetId);
        RulesetSteps.RefuseNewInactiveLinks(RulesetSteps.Links(r.Steps), RulesetSteps.Links(byId[rulesetId].Steps), byId.Values);

        await UniqueName(r.Name, () => cn.ExecuteAsync(new CommandDefinition(
            "UPDATE Rulesets SET Name = @Name, Description = @Description, UpdatedAt = UTC_TIMESTAMP() WHERE RulesetId = @Id",
            new { Id = rulesetId, r.Name, r.Description },
            transaction: tx, cancellationToken: ct)));

        // Cascades to RulesetStepParams.
        await cn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM RulesetSteps WHERE RulesetId = @Id",
            new { Id = rulesetId }, transaction: tx, cancellationToken: ct));

        await InsertStepsAsync(cn, tx, rulesetId, r.Steps, ct);
        await tx.CommitAsync(ct);
        return (await ReadAsync(cn, null, rulesetId, ct)).Single();
    }

    /// <summary>False when there is no such ruleset. Throws while a ruleset or mapping links to it.</summary>
    public async Task<bool> DeleteAsync(int rulesetId, CancellationToken ct = default)
    {
        await using var cn = await _db.OpenStagingAsync(ct);
        await using var tx = await cn.BeginTransactionAsync(ct);

        var name = await cn.ExecuteScalarAsync<string?>(new CommandDefinition(
            "SELECT Name FROM Rulesets WHERE RulesetId = @Id FOR UPDATE",
            new { Id = rulesetId }, transaction: tx, cancellationToken: ct));
        if (name is null)
            return false;

        // Ids are stored as their canonical digits (CleanStep), so a string match finds every link.
        var args = new { Kind = TransformKinds.Ruleset, Param = RulesetSteps.IdParam, Value = rulesetId.ToString(System.Globalization.CultureInfo.InvariantCulture) };
        var rulesets = await cn.QueryAsync<string>(new CommandDefinition(
            """
            SELECT DISTINCT CONCAT('ruleset ''', r.Name, '''')
            FROM RulesetSteps s
            JOIN RulesetStepParams p ON p.RulesetStepId = s.RulesetStepId
            JOIN Rulesets r ON r.RulesetId = s.RulesetId
            WHERE s.Kind = @Kind AND p.Name = @Param AND p.Value = @Value
            """,
            args, transaction: tx, cancellationToken: ct));
        var groups = await cn.QueryAsync<string>(new CommandDefinition(
            $"""
            SELECT DISTINCT CONCAT('mapping group ''', g.Name, '''')
            FROM ({MappingReferences}) m JOIN MappingGroups g ON g.MappingGroupId = m.MappingGroupId
            WHERE m.RulesetId = @Id
            """,
            new { Id = rulesetId }, transaction: tx, cancellationToken: ct));

        var users = rulesets.Concat(groups).ToList();
        if (users.Count > 0)
            throw new ArgumentException($"Ruleset '{name}' is used by {string.Join(", ", users)}. Remove it from them first.");

        await cn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM Rulesets WHERE RulesetId = @Id",
            new { Id = rulesetId }, transaction: tx, cancellationToken: ct));
        await tx.CommitAsync(ct);
        return true;
    }

    public static Ruleset Clean(Ruleset ruleset)
    {
        var name = (ruleset.Name ?? "").Trim();
        if (name.Length == 0)
            throw new ArgumentException("A ruleset needs a name.", nameof(ruleset));
        if (name.Length > 100)
            throw new ArgumentException("A ruleset name can be at most 100 characters.", nameof(ruleset));

        return new Ruleset
        {
            Name = name,
            Description = string.IsNullOrWhiteSpace(ruleset.Description) ? null : ruleset.Description.Trim(),
            Steps = CleanSteps(ruleset.Steps),
        };
    }

    public static List<TransformStep> CleanSteps(List<TransformStep>? steps)
    {
        if (steps is null || steps.Count == 0)
            throw new ArgumentException("A ruleset needs at least one transform.", nameof(steps));
        return steps.Select((s, i) => CleanStep(s, i + 1)).ToList();
    }

    private static TransformStep CleanStep(TransformStep step, int number)
    {
        var kindName = (step.Kind ?? "").Trim().ToLowerInvariant();
        if (!TransformKinds.TryGet(kindName, out var kind))
            throw new ArgumentException($"Transform {number}: unknown kind '{step.Kind}'.", nameof(step));

        // Values are kept verbatim: whitespace can be meaningful, e.g. a " " separator.
        var supplied = step.Params ?? [];
        var cleaned = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var p in kind.Params)
        {
            var value = supplied.TryGetValue(p.Name, out var v) && !string.IsNullOrEmpty(v) ? v : null;
            if (value is null && p.Required)
                throw new ArgumentException($"Transform {number} ({kindName}) needs '{p.Name}'.", nameof(step));
            value ??= p.Default;
            if (value is { Length: > 1024 })
                throw new ArgumentException($"Transform {number} ({kindName}): '{p.Name}' can be at most 1024 characters.", nameof(step));
            if (!string.IsNullOrEmpty(value))
                cleaned[p.Name] = value;
        }

        if (kindName is TransformKinds.RegexReplace or TransformKinds.RegexExtract)
        {
            try
            {
                _ = new System.Text.RegularExpressions.Regex(cleaned["pattern"]);
            }
            catch (ArgumentException ex)
            {
                throw new ArgumentException($"Transform {number} ({kindName}): the pattern is not valid. {ex.Message}", nameof(step));
            }
        }

        if (kindName is TransformKinds.Ruleset)
        {
            if (!RulesetSteps.TryParseId(cleaned[RulesetSteps.IdParam], out var linkedId))
                throw new ArgumentException($"Transform {number} ({kindName}): '{RulesetSteps.IdParam}' has to be a ruleset id.", nameof(step));
            cleaned[RulesetSteps.IdParam] = linkedId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return new TransformStep { Kind = kindName, Params = cleaned };
    }

    private static async Task<T> UniqueName<T>(string name, Func<Task<T>> write)
    {
        try
        {
            return await write();
        }
        catch (MySqlException ex) when (ex.Number == DuplicateKeyErrorCode)
        {
            throw new ArgumentException($"There is already a ruleset named '{name}'.");
        }
    }

    private static async Task InsertStepsAsync(
        MySqlConnection cn, MySqlTransaction tx, int rulesetId, List<TransformStep> steps, CancellationToken ct)
    {
        for (var position = 0; position < steps.Count; position++)
        {
            var step = steps[position];
            var stepId = await cn.ExecuteScalarAsync<int>(new CommandDefinition(
                """
                INSERT INTO RulesetSteps (RulesetId, Kind, Position) VALUES (@RulesetId, @Kind, @Position);
                SELECT LAST_INSERT_ID();
                """,
                new { RulesetId = rulesetId, step.Kind, Position = position },
                transaction: tx, cancellationToken: ct));

            if (step.Params.Count > 0)
                await cn.ExecuteAsync(new CommandDefinition(
                    "INSERT INTO RulesetStepParams (RulesetStepId, Name, Value) VALUES (@RulesetStepId, @Name, @Value)",
                    step.Params.Select(p => new { RulesetStepId = stepId, Name = p.Key, p.Value }).ToList(),
                    transaction: tx, cancellationToken: ct));
        }
    }

    // Serializes link checks, so concurrent saves cannot form a loop or link to a ruleset being deleted or deactivated.
    private static async Task<List<int>> LockAllAsync(MySqlConnection cn, MySqlTransaction tx, CancellationToken ct) =>
        (await cn.QueryAsync<int>(new CommandDefinition(
            "SELECT RulesetId FROM Rulesets ORDER BY RulesetId FOR UPDATE",
            transaction: tx, cancellationToken: ct))).ToList();

    private static async Task<IReadOnlyDictionary<int, Ruleset>> ByIdAsync(MySqlConnection cn, MySqlTransaction? tx, CancellationToken ct) =>
        (await ReadAsync(cn, tx, new Query(), ct)).ToDictionary(r => r.RulesetId);

    // Applied to Rulesets as r. The column's collation makes the name match ignore case and accents.
    private const string Filter =
        "(@Id IS NULL OR r.RulesetId = @Id) AND (@ActiveOnly = FALSE OR r.IsActive) AND (@Pattern IS NULL OR r.Name LIKE @Pattern)";

    private sealed record Query(int? Id = null, bool ActiveOnly = false, string? Pattern = null)
    {
        public static Query For(bool activeOnly, string? search) => new(null, activeOnly, ContainsPattern(search));
    }

    private static string? ContainsPattern(string? search)
    {
        var text = search?.Trim();
        if (string.IsNullOrEmpty(text))
            return null;
        return "%" + text.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_") + "%";
    }

    private static Task<List<Ruleset>> ReadAsync(MySqlConnection cn, MySqlTransaction? tx, int? rulesetId, CancellationToken ct) =>
        ReadAsync(cn, tx, new Query(rulesetId), ct);

    private static async Task<List<Ruleset>> ReadAsync(MySqlConnection cn, MySqlTransaction? tx, Query args, CancellationToken ct)
    {
        var rulesets = (await cn.QueryAsync<Ruleset>(new CommandDefinition(
            $"""
            SELECT r.RulesetId, r.Name, r.Description, r.IsActive, r.CreatedAt, r.UpdatedAt FROM Rulesets r
            WHERE {Filter} ORDER BY r.Name
            """,
            args, transaction: tx, cancellationToken: ct))).ToList();
        if (rulesets.Count == 0)
            return rulesets;

        var steps = await cn.QueryAsync<(int RulesetStepId, int RulesetId, string Kind)>(new CommandDefinition(
            $"""
            SELECT s.RulesetStepId, s.RulesetId, s.Kind
            FROM RulesetSteps s JOIN Rulesets r ON r.RulesetId = s.RulesetId
            WHERE {Filter} ORDER BY s.RulesetId, s.Position
            """,
            args, transaction: tx, cancellationToken: ct));

        var stepParams = await cn.QueryAsync<(int RulesetStepId, string Name, string Value)>(new CommandDefinition(
            $"""
            SELECT p.RulesetStepId, p.Name, p.Value
            FROM RulesetStepParams p
            JOIN RulesetSteps s ON p.RulesetStepId = s.RulesetStepId
            JOIN Rulesets r ON r.RulesetId = s.RulesetId
            WHERE {Filter}
            """,
            args, transaction: tx, cancellationToken: ct));

        var byId = rulesets.ToDictionary(r => r.RulesetId);
        var stepById = new Dictionary<int, TransformStep>();
        foreach (var s in steps)
        {
            var step = new TransformStep { Kind = s.Kind };
            stepById[s.RulesetStepId] = step;
            byId[s.RulesetId].Steps.Add(step);
        }

        foreach (var p in stepParams)
            stepById[p.RulesetStepId].Params[p.Name] = p.Value;

        return rulesets;
    }
}
