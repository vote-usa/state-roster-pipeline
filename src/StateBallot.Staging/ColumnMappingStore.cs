using Dapper;

namespace StateBallot.Staging;

public sealed class ColumnMappingStore
{
    private const string GroupSelect =
        "SELECT MappingGroupId, Name, StateCode, Year, ElectionType, Party, Description, CreatedAt, UpdatedAt FROM MappingGroups";

    private static readonly IReadOnlyDictionary<string, int> CatalogOrder = CandidateTargetColumns.All
        .Select((c, i) => (c.Name, i))
        .ToDictionary(x => x.Name, x => x.i, StringComparer.Ordinal);

    private readonly StagingDb _db;

    public ColumnMappingStore(StagingDb db) => _db = db;

    public async Task<IReadOnlyList<MappingGroup>> ListGroupsAsync(string? stateCode = null, CancellationToken ct = default)
    {
        var state = stateCode is null ? null : NormalizeState(stateCode);
        await using var cn = await _db.OpenStagingAsync(ct);
        var groups = await cn.QueryAsync<MappingGroup>(new CommandDefinition(
            $"{GroupSelect} WHERE (@State IS NULL OR StateCode = @State) ORDER BY MappingGroupId DESC",
            new { State = state },
            cancellationToken: ct));
        return groups.ToList();
    }

    public async Task<MappingGroup?> GetGroupAsync(int groupId, CancellationToken ct = default)
    {
        await using var cn = await _db.OpenStagingAsync(ct);
        return await ReadGroupAsync(cn, groupId, ct);
    }

    public async Task<MappingGroup> CreateGroupAsync(MappingGroup group, CancellationToken ct = default)
    {
        var g = Normalize(group);
        await using var cn = await _db.OpenStagingAsync(ct);
        var id = await cn.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            INSERT INTO MappingGroups (Name, StateCode, Year, ElectionType, Party, Description, CreatedAt, UpdatedAt)
            VALUES (@Name, @StateCode, @Year, @ElectionType, @Party, @Description, UTC_TIMESTAMP(), UTC_TIMESTAMP());
            SELECT LAST_INSERT_ID();
            """,
            new { g.Name, g.StateCode, g.Year, g.ElectionType, g.Party, g.Description },
            cancellationToken: ct));
        return (await ReadGroupAsync(cn, id, ct))!;
    }

    public async Task<MappingGroup> CloneGroupAsync(int sourceGroupId, MappingGroup target, CancellationToken ct = default)
    {
        if (await GetGroupAsync(sourceGroupId, ct) is null)
            throw new ArgumentException($"No mapping group with id {sourceGroupId}.", nameof(sourceGroupId));

        var columns = await GetColumnsAsync(sourceGroupId, ct);
        var created = await CreateGroupAsync(target, ct);
        if (columns.Count > 0)
            await SaveColumnsAsync(created.MappingGroupId, columns, ct);
        return created;
    }

    public async Task<List<TargetColumnMapping>> GetColumnsAsync(int groupId, CancellationToken ct = default)
    {
        await using var cn = await _db.OpenStagingAsync(ct);

        var targets = (await cn.QueryAsync<(int TargetMappingId, string TargetColumn)>(new CommandDefinition(
            "SELECT TargetMappingId, TargetColumn FROM TargetMappings WHERE MappingGroupId = @Id",
            new { Id = groupId }, cancellationToken: ct))).ToList();

        var inputs = await cn.QueryAsync<(int MappingInputId, int TargetMappingId, string Kind, string? SourceColumn, string? ManualValue)>(new CommandDefinition(
            """
            SELECT i.MappingInputId, i.TargetMappingId, i.Kind, i.SourceColumn, i.ManualValue
            FROM MappingInputs i JOIN TargetMappings t ON i.TargetMappingId = t.TargetMappingId
            WHERE t.MappingGroupId = @Id ORDER BY i.TargetMappingId, i.Position
            """,
            new { Id = groupId }, cancellationToken: ct));

        var targetRulesets = await cn.QueryAsync<(int TargetMappingId, int RulesetId)>(new CommandDefinition(
            """
            SELECT r.TargetMappingId, r.RulesetId
            FROM TargetMappingRulesets r JOIN TargetMappings t ON r.TargetMappingId = t.TargetMappingId
            WHERE t.MappingGroupId = @Id ORDER BY r.Position
            """,
            new { Id = groupId }, cancellationToken: ct));

        var inputRulesets = await cn.QueryAsync<(int MappingInputId, int RulesetId)>(new CommandDefinition(
            """
            SELECT r.MappingInputId, r.RulesetId
            FROM MappingInputRulesets r
            JOIN MappingInputs i ON r.MappingInputId = i.MappingInputId
            JOIN TargetMappings t ON i.TargetMappingId = t.TargetMappingId
            WHERE t.MappingGroupId = @Id ORDER BY r.Position
            """,
            new { Id = groupId }, cancellationToken: ct));

        var byTarget = targets.ToDictionary(
            t => t.TargetMappingId,
            t => new TargetColumnMapping { TargetColumn = t.TargetColumn });
        var inputById = new Dictionary<int, MappingInput>();

        foreach (var i in inputs)
        {
            var input = new MappingInput { Kind = i.Kind, SourceColumn = i.SourceColumn, ManualValue = i.ManualValue };
            inputById[i.MappingInputId] = input;
            byTarget[i.TargetMappingId].Inputs.Add(input);
        }

        foreach (var r in targetRulesets)
            byTarget[r.TargetMappingId].RulesetIds.Add(r.RulesetId);
        foreach (var r in inputRulesets)
            inputById[r.MappingInputId].RulesetIds.Add(r.RulesetId);

        return byTarget.Values
            .OrderBy(c => CatalogOrder.GetValueOrDefault(c.TargetColumn, int.MaxValue))
            .ThenBy(c => c.TargetColumn, StringComparer.Ordinal)
            .ToList();
    }

    public async Task SaveColumnsAsync(int groupId, List<TargetColumnMapping> columns, CancellationToken ct = default)
    {
        var entries = Clean(columns);

        await using var cn = await _db.OpenStagingAsync(ct);
        await using var tx = await cn.BeginTransactionAsync(ct);

        var touched = await cn.ExecuteAsync(new CommandDefinition(
            "UPDATE MappingGroups SET UpdatedAt = UTC_TIMESTAMP() WHERE MappingGroupId = @Id",
            new { Id = groupId },
            transaction: tx, cancellationToken: ct));
        if (touched == 0)
            throw new ArgumentException($"No mapping group with id {groupId}.", nameof(groupId));

        // RulesetStore already refuses loops, so only existence and deactivation are checked here.
        var links = entries.SelectMany(RulesetSteps.RulesetIds).Distinct().ToList();
        if (links.Count > 0)
        {
            var found = (await cn.QueryAsync<Ruleset>(new CommandDefinition(
                "SELECT RulesetId, Name, IsActive FROM Rulesets WHERE RulesetId IN @Ids FOR UPDATE",
                new { Ids = links },
                transaction: tx, cancellationToken: ct))).ToList();
            var missing = links.Except(found.Select(r => r.RulesetId)).ToList();
            if (missing.Count > 0)
                throw new ArgumentException($"No ruleset {string.Join(", ", missing)}.", nameof(columns));

            var linkedBefore = await cn.QueryAsync<int>(new CommandDefinition(
                $"SELECT g.RulesetId FROM ({RulesetStore.MappingReferences}) g WHERE g.MappingGroupId = @Id",
                new { Id = groupId },
                transaction: tx, cancellationToken: ct));
            RulesetSteps.RefuseNewInactiveLinks(links, linkedBefore, found);
        }

        // Cascades to MappingInputs and both ruleset reference tables.
        await cn.ExecuteAsync(new CommandDefinition(
            "DELETE FROM TargetMappings WHERE MappingGroupId = @Id",
            new { Id = groupId },
            transaction: tx, cancellationToken: ct));

        foreach (var entry in entries)
        {
            var targetId = await cn.ExecuteScalarAsync<int>(new CommandDefinition(
                """
                INSERT INTO TargetMappings (MappingGroupId, TargetColumn) VALUES (@MappingGroupId, @TargetColumn);
                SELECT LAST_INSERT_ID();
                """,
                new { MappingGroupId = groupId, entry.TargetColumn },
                transaction: tx, cancellationToken: ct));

            for (var position = 0; position < entry.Inputs.Count; position++)
            {
                var input = entry.Inputs[position];
                var inputId = await cn.ExecuteScalarAsync<int>(new CommandDefinition(
                    """
                    INSERT INTO MappingInputs (TargetMappingId, Kind, SourceColumn, ManualValue, Position)
                    VALUES (@TargetMappingId, @Kind, @SourceColumn, @ManualValue, @Position);
                    SELECT LAST_INSERT_ID();
                    """,
                    new { TargetMappingId = targetId, input.Kind, input.SourceColumn, input.ManualValue, Position = position },
                    transaction: tx, cancellationToken: ct));

                if (input.RulesetIds.Count > 0)
                    await cn.ExecuteAsync(new CommandDefinition(
                        "INSERT INTO MappingInputRulesets (MappingInputId, RulesetId, Position) VALUES (@MappingInputId, @RulesetId, @Position)",
                        input.RulesetIds.Select((id, p) => new { MappingInputId = inputId, RulesetId = id, Position = p }).ToList(),
                        transaction: tx, cancellationToken: ct));
            }

            if (entry.RulesetIds.Count > 0)
                await cn.ExecuteAsync(new CommandDefinition(
                    "INSERT INTO TargetMappingRulesets (TargetMappingId, RulesetId, Position) VALUES (@TargetMappingId, @RulesetId, @Position)",
                    entry.RulesetIds.Select((id, p) => new { TargetMappingId = targetId, RulesetId = id, Position = p }).ToList(),
                    transaction: tx, cancellationToken: ct));
        }

        await tx.CommitAsync(ct);
    }

    private static async Task<MappingGroup?> ReadGroupAsync(
        System.Data.Common.DbConnection cn, int groupId, CancellationToken ct) =>
        await cn.QuerySingleOrDefaultAsync<MappingGroup>(new CommandDefinition(
            $"{GroupSelect} WHERE MappingGroupId = @Id", new { Id = groupId }, cancellationToken: ct));

    private static MappingGroup Normalize(MappingGroup group)
    {
        if (string.IsNullOrWhiteSpace(group.Name))
            throw new ArgumentException("A mapping group needs a name.", nameof(group));
        return new MappingGroup
        {
            MappingGroupId = group.MappingGroupId,
            Name = group.Name.Trim(),
            StateCode = NormalizeState(group.StateCode),
            Year = group.Year,
            ElectionType = Blank(group.ElectionType),
            Party = Blank(group.Party),
            Description = Blank(group.Description),
        };
    }

    private static List<TargetColumnMapping> Clean(List<TargetColumnMapping> columns)
    {
        var cleaned = new List<TargetColumnMapping>();
        foreach (var column in columns)
        {
            if (!CandidateTargetColumns.Contains(column.TargetColumn))
                throw new ArgumentException(
                    $"'{column.TargetColumn}' is not a candidate target column.", nameof(columns));

            var inputs = column.Inputs
                .Select(CleanInput)
                .OfType<MappingInput>()
                .ToList();

            if (inputs.Count == 0)
                continue;

            cleaned.Add(new TargetColumnMapping
            {
                TargetColumn = column.TargetColumn,
                Inputs = inputs,
                RulesetIds = CleanRulesetIds(column.RulesetIds),
            });
        }

        return cleaned;
    }

    private static List<int> CleanRulesetIds(List<int>? ids)
    {
        var list = ids ?? [];
        if (list.Any(id => id <= 0))
            throw new ArgumentException($"Ruleset ids must be positive, got {string.Join(", ", list.Where(id => id <= 0))}.", nameof(ids));
        return list.ToList();
    }

    private static MappingInput? CleanInput(MappingInput input)
    {
        var kind = (input.Kind ?? "").Trim().ToLowerInvariant();
        var rulesetIds = CleanRulesetIds(input.RulesetIds);
        return kind switch
        {
            MappingInputKinds.Column => Blank(input.SourceColumn) is { } c
                ? new MappingInput { Kind = MappingInputKinds.Column, SourceColumn = c, RulesetIds = rulesetIds } : null,
            MappingInputKinds.Manual => Blank(input.ManualValue) is { } v
                ? new MappingInput { Kind = MappingInputKinds.Manual, ManualValue = v, RulesetIds = rulesetIds } : null,
            _ => throw new ArgumentException($"Unknown mapping input kind '{input.Kind}'.", nameof(input)),
        };
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string NormalizeState(string stateCode)
    {
        var state = (stateCode ?? "").Trim().ToUpperInvariant();
        if (state.Length != 2)
            throw new ArgumentException($"State code must be two letters, got '{stateCode}'.", nameof(stateCode));
        return state;
    }
}
