using System.Security.Cryptography;
using System.Text;
using Dapper;
using MySqlConnector;

namespace StateBallot.Staging;

/// <summary>
/// Applies plain SQL migration files from db/migrations/ to the staging schema in
/// filename order. Each applied file is recorded in SchemaMigrations with a checksum;
/// an applied file whose content later changes is an error (migrations are additive).
/// Creates the schema itself when it does not exist yet.
/// </summary>
public sealed class Migrator
{
    public const string MigrationsDirEnvVar = "ROSTER_MIGRATIONS_DIR";

    private readonly string _connectionString;
    private readonly string _migrationsDir;

    public Migrator(string stagingConnectionString, string migrationsDir)
    {
        _connectionString = stagingConnectionString;
        _migrationsDir = migrationsDir;
    }

    public sealed record Report(IReadOnlyList<string> Applied, IReadOnlyList<string> AlreadyApplied);

    public async Task<Report> ApplyAsync(TextWriter log, CancellationToken ct = default)
    {
        if (!Directory.Exists(_migrationsDir))
            throw new InvalidOperationException($"Migrations directory not found: {_migrationsDir}");

        var files = Directory.EnumerateFiles(_migrationsDir, "*.sql")
            .OrderBy(Path.GetFileName, StringComparer.Ordinal)
            .ToList();

        await EnsureDatabaseAsync(ct);

        await using var cn = new MySqlConnection(_connectionString);
        await cn.OpenAsync(ct);

        await cn.ExecuteAsync(
            """
            CREATE TABLE IF NOT EXISTS `SchemaMigrations` (
              `Name` VARCHAR(200) NOT NULL,
              `Checksum` CHAR(64) NOT NULL,
              `AppliedAt` DATETIME NOT NULL,
              PRIMARY KEY (`Name`)
            ) ENGINE=InnoDB DEFAULT CHARSET=utf8mb4 COLLATE=utf8mb4_unicode_ci;
            """);

        var recorded = (await cn.QueryAsync<(string Name, string Checksum)>(
                "SELECT `Name`, `Checksum` FROM `SchemaMigrations`"))
            .ToDictionary(r => r.Name, r => r.Checksum, StringComparer.Ordinal);

        var applied = new List<string>();
        var skipped = new List<string>();

        foreach (var file in files)
        {
            var name = Path.GetFileName(file);
            var sql = await File.ReadAllTextAsync(file, ct);
            var checksum = Sha256Hex(sql);

            if (recorded.TryGetValue(name, out var existing))
            {
                if (!string.Equals(existing, checksum, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException(
                        $"Migration {name} was already applied with a different checksum. " +
                        "Applied migrations are immutable; add a new file instead.");
                skipped.Add(name);
                continue;
            }

            log.WriteLine($"Applying {name}...");
            // MySQL DDL auto-commits statement by statement; the transaction still keeps
            // the SchemaMigrations insert tied to a fully executed script.
            await using (var tx = await cn.BeginTransactionAsync(ct))
            {
                await cn.ExecuteAsync(new CommandDefinition(sql, transaction: tx, cancellationToken: ct));
                await cn.ExecuteAsync(new CommandDefinition(
                    "INSERT INTO `SchemaMigrations` (`Name`, `Checksum`, `AppliedAt`) VALUES (@Name, @Checksum, UTC_TIMESTAMP())",
                    new { Name = name, Checksum = checksum },
                    transaction: tx,
                    cancellationToken: ct));
                await tx.CommitAsync(ct);
            }

            applied.Add(name);
        }

        return new Report(applied, skipped);
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

    /// <summary>
    /// db/migrations/ next to src/ and data/, walking up from the executable; or
    /// ROSTER_MIGRATIONS_DIR when set.
    /// </summary>
    public static string FindMigrationsDir()
    {
        var fromEnv = Environment.GetEnvironmentVariable(MigrationsDirEnvVar);
        if (!string.IsNullOrWhiteSpace(fromEnv))
            return Path.GetFullPath(fromEnv);

        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d is not null; d = d.Parent)
        {
            var candidate = Path.Combine(d.FullName, "db", "migrations");
            if (Directory.Exists(candidate) && Directory.Exists(Path.Combine(d.FullName, "src")))
                return candidate;
        }

        return Path.Combine(Environment.CurrentDirectory, "db", "migrations");
    }

    private static string Sha256Hex(string text)
    {
        var normalized = text.Replace("\r\n", "\n");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
    }
}
