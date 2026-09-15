using Dapper;
using FluentMigrator.Runner;
using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;

namespace StateBallot.Staging;

/// <summary>
/// Applies the FluentMigrator migrations in this assembly to the staging schema.
/// Migration classes live in Migrations/ and carry a [Migration(n)] version. The runner
/// applies the ones VersionInfo does not already record. Creates the schema itself when
/// it does not exist yet, which FluentMigrator does not do.
/// </summary>
public sealed class Migrator
{
    private readonly string _connectionString;

    public Migrator(string stagingConnectionString)
    {
        _connectionString = stagingConnectionString;
    }

    public sealed record Report(long Applied, long AlreadyApplied, long CurrentVersion);

    public async Task<Report> ApplyAsync(CancellationToken ct = default)
    {
        await EnsureDatabaseAsync(ct);

        var before = await AppliedVersionsAsync(ct);

        using var provider = BuildServiceProvider();
        using var scope = provider.CreateScope();
        var runner = scope.ServiceProvider.GetRequiredService<IMigrationRunner>();

        if (runner.HasMigrationsToApplyUp())
            runner.MigrateUp();

        var after = await AppliedVersionsAsync(ct);

        return new Report(after.Count - before.Count, before.Count, after.Count == 0 ? 0 : after.Max());
    }

    private ServiceProvider BuildServiceProvider() => new ServiceCollection()
        .AddFluentMigratorCore()
        .ConfigureRunner(rb => rb
            .AddMySql8()
            .WithGlobalConnectionString(_connectionString)
            .ScanIn(typeof(Migrator).Assembly).For.Migrations())
        .AddLogging(lb => lb.AddFluentMigratorConsole())
        .BuildServiceProvider(validateScopes: false);

    /// <summary>Versions already recorded, empty when the schema has no VersionInfo table yet.</summary>
    private async Task<List<long>> AppliedVersionsAsync(CancellationToken ct)
    {
        await using var cn = new MySqlConnection(_connectionString);
        await cn.OpenAsync(ct);

        var exists = await cn.ExecuteScalarAsync<long>(new CommandDefinition(
            "SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'VersionInfo'",
            cancellationToken: ct));
        if (exists == 0)
            return [];

        return (await cn.QueryAsync<long>(new CommandDefinition(
            "SELECT `Version` FROM `VersionInfo`", cancellationToken: ct))).ToList();
    }

    /// <summary>Creates the schema named in the connection string if missing.</summary>
    private async Task EnsureDatabaseAsync(CancellationToken ct)
    {
        var builder = new MySqlConnectionStringBuilder(_connectionString);
        var database = builder.Database;
        if (string.IsNullOrWhiteSpace(database))
            throw new InvalidOperationException("Staging connection string must name a Database.");
        if (database.Any(c => !(char.IsLetterOrDigit(c) || c == '_')))
            throw new InvalidOperationException($"Unsafe staging database name '{database}'.");

        builder.Database = "";
        await using var cn = new MySqlConnection(builder.ConnectionString);
        await cn.OpenAsync(ct);
        await cn.ExecuteAsync(
            $"CREATE DATABASE IF NOT EXISTS `{database}` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci");
    }
}
