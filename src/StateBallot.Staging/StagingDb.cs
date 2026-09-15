using MySqlConnector;

namespace StateBallot.Staging;

/// <summary>
/// Connection strings for the two schemas the pipeline talks to: the pipeline's own
/// roster_staging schema and the VoteUSA-shaped vote schema (local Docker by default,
/// the VoteProject instance in production). Read from environment variables so the
/// CLI and the console API share one configuration.
/// </summary>
public sealed class StagingDb
{
    public const string StagingEnvVar = "ROSTER_STAGING_CONNECTION";
    public const string VoteEnvVar = "VOTE_CONNECTION";

    public const string DefaultStagingConnection =
        "Server=localhost;Port=3307;Database=roster_staging;User ID=roster;Password=roster;";

    public const string DefaultVoteConnection =
        "Server=localhost;Port=3307;Database=vote;User ID=roster;Password=roster;";

    public string StagingConnectionString { get; }
    public string VoteConnectionString { get; }

    public StagingDb(string stagingConnectionString, string voteConnectionString)
    {
        StagingConnectionString = stagingConnectionString;
        VoteConnectionString = voteConnectionString;
    }

    public static StagingDb FromEnvironment() => new(
        Environment.GetEnvironmentVariable(StagingEnvVar) is { Length: > 0 } s ? s : DefaultStagingConnection,
        Environment.GetEnvironmentVariable(VoteEnvVar) is { Length: > 0 } v ? v : DefaultVoteConnection);

    public async Task<MySqlConnection> OpenStagingAsync(CancellationToken ct = default)
    {
        var cn = new MySqlConnection(StagingConnectionString);
        await cn.OpenAsync(ct);
        return cn;
    }

    public async Task<MySqlConnection> OpenVoteAsync(CancellationToken ct = default)
    {
        var cn = new MySqlConnection(VoteConnectionString);
        await cn.OpenAsync(ct);
        return cn;
    }

    /// <summary>Redacts the password for logs.</summary>
    public static string Describe(string connectionString)
    {
        var b = new MySqlConnectionStringBuilder(connectionString);
        return $"{b.Server}:{b.Port}/{b.Database} as {b.UserID}";
    }
}
