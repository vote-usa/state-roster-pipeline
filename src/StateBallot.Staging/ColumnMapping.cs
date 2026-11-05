namespace StateBallot.Staging;

public sealed class MappingGroup
{
    public int MappingGroupId { get; set; }
    public string Name { get; set; } = "";
    public string StateCode { get; set; } = "";
    public int? Year { get; set; }
    public string? ElectionType { get; set; }
    public string? Party { get; set; }
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public static class MappingInputKinds
{
    public const string Column = "column";
    public const string Manual = "manual";
}

public sealed class MappingInput
{
    public string Kind { get; set; } = MappingInputKinds.Column;
    public string? SourceColumn { get; set; }
    public string? ManualValue { get; set; }

    /// <summary>Pipeline applied to this input alone before the inputs are combined.</summary>
    public List<TransformStep> Transform { get; set; } = new();
}

public sealed class TransformStep
{
    public string Kind { get; set; } = "";
    public Dictionary<string, string> Params { get; set; } = new();
}

public sealed class TargetColumnMapping
{
    public string TargetColumn { get; set; } = "";
    public List<MappingInput> Inputs { get; set; } = new();

    /// <summary>Ordered pipeline applied to the inputs to produce the target value. Empty = pass the single input through.</summary>
    public List<TransformStep> Transform { get; set; } = new();
}
