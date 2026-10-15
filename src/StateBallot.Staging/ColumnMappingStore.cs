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

        var steps = await cn.QueryAsync<(int TransformStepId, int TargetMappingId, int? MappingInputId, string Kind)>(new CommandDefinition(
            """
            SELECT s.TransformStepId, s.TargetMappingId, s.MappingInputId, s.Kind
            FROM TransformSteps s JOIN TargetMappings t ON s.TargetMappingId = t.TargetMappingId
            WHERE t.MappingGroupId = @Id ORDER BY s.Position
            """,
            new { Id = groupId }, cancellationToken: ct));

        var stepParams = await cn.QueryAsync<(int TransformStepId, string Name, string Value)>(new CommandDefinition(
            """
            SELECT p.TransformStepId, p.Name, p.Value
            FROM TransformStepParams p
            JOIN TransformSteps s ON p.TransformStepId = s.TransformStepId
            JOIN TargetMappings t ON s.TargetMappingId = t.TargetMappingId
            WHERE t.MappingGroupId = @Id
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

        var stepById = new Dictionary<int, TransformStep>();
        foreach (var s in steps)
        {
            var step = new TransformStep { Kind = s.Kind };
            stepById[s.TransformStepId] = step;
            if (s.MappingInputId is { } inputId && inputById.TryGetValue(inputId, out var owner))
                owner.Transform.Add(step);
            else
                byTarget[s.TargetMappingId].Transform.Add(step);
        }

        foreach (var p in stepParams)
            if (stepById.TryGetValue(p.TransformStepId, out var step))
                step.Params[p.Name] = p.Value;

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

        // Cascades to MappingInputs, TransformSteps and their params.
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

                await InsertStepsAsync(cn, tx, targetId, inputId, input.Transform, ct);
            }

            await InsertStepsAsync(cn, tx, targetId, null, entry.Transform, ct);
        }

        await tx.CommitAsync(ct);
    }

    private static async Task InsertStepsAsync(
        System.Data.Common.DbConnection cn, System.Data.Common.DbTransaction tx,
        int targetId, int? mappingInputId, List<TransformStep> steps, CancellationToken ct)
    {
        for (var position = 0; position < steps.Count; position++)
        {
            var step = steps[position];
            var stepId = await cn.ExecuteScalarAsync<int>(new CommandDefinition(
                """
                INSERT INTO TransformSteps (TargetMappingId, MappingInputId, Kind, Position)
                VALUES (@TargetMappingId, @MappingInputId, @Kind, @Position);
                SELECT LAST_INSERT_ID();
                """,
                new { TargetMappingId = targetId, MappingInputId = mappingInputId, step.Kind, Position = position },
                transaction: tx, cancellationToken: ct));

            if (step.Params.Count > 0)
                await cn.ExecuteAsync(new CommandDefinition(
                    "INSERT INTO TransformStepParams (TransformStepId, Name, Value) VALUES (@TransformStepId, @Name, @Value)",
                    step.Params.Select(p => new { TransformStepId = stepId, Name = p.Key, Value = p.Value }).ToList(),
                    transaction: tx, cancellationToken: ct));
        }
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
                Transform = column.Transform.Select(CleanStep).ToList(),
            });
        }

        return cleaned;
    }

    private static TransformStep CleanStep(TransformStep step)
    {
        var kindName = (step.Kind ?? "").Trim().ToLowerInvariant();
        if (!TransformKinds.TryGet(kindName, out var kind))
            throw new ArgumentException($"Unknown transform kind '{step.Kind}'.", nameof(step));

        // Param values are stored verbatim - whitespace can be meaningful (e.g. a " " separator).
        var cleaned = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var p in kind.Params)
        {
            var value = step.Params.TryGetValue(p.Name, out var v) ? v : null;
            if (string.IsNullOrEmpty(value))
            {
                if (p.Required)
                    throw new ArgumentException($"Transform '{kindName}' requires '{p.Name}'.", nameof(step));
                value = p.Default;
            }
            if (!string.IsNullOrEmpty(value))
                cleaned[p.Name] = value;
        }

        return new TransformStep { Kind = kindName, Params = cleaned };
    }

    private static MappingInput? CleanInput(MappingInput input)
    {
        var kind = (input.Kind ?? "").Trim().ToLowerInvariant();
        var transform = input.Transform.Select(CleanStep).ToList();
        return kind switch
        {
            MappingInputKinds.Column => Blank(input.SourceColumn) is { } c
                ? new MappingInput { Kind = MappingInputKinds.Column, SourceColumn = c, Transform = transform } : null,
            MappingInputKinds.Manual => Blank(input.ManualValue) is { } v
                ? new MappingInput { Kind = MappingInputKinds.Manual, ManualValue = v, Transform = transform } : null,
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
