namespace StateBallot.Core.Publishing;

/// <summary>What the caller knows about the run that the output files cannot tell on their own.</summary>
public sealed record RunContext(int Year)
{
    public string? Wayback { get; init; }
    public string? CliArgs { get; init; }
    public string RequestedBy { get; init; } = Environment.UserName;
}
