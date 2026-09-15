using System.Reflection;
using FluentMigrator;

namespace StateBallot.Staging.Tests;

public class MigrationTests
{
    private static readonly (long Version, string Name)[] Migrations = typeof(Migrator).Assembly
        .GetTypes()
        .Where(t => typeof(Migration).IsAssignableFrom(t) && !t.IsAbstract)
        .Select(t => (t.GetCustomAttribute<MigrationAttribute>()?.Version
                      ?? throw new InvalidOperationException($"{t.Name} has no [Migration] attribute"),
                      t.Name))
        .OrderBy(m => m.Item1)
        .ToArray();

    [Fact]
    public void AssemblyScan_FindsMigrations()
    {
        // The runner discovers migrations by scanning this assembly, so an empty result
        // would make --migrate a silent no-op.
        Assert.NotEmpty(Migrations);
    }

    [Fact]
    public void Versions_AreUnique()
    {
        // FluentMigrator only fails on a duplicate version when the runner builds.
        long[] versions = [.. Migrations.Select(m => m.Version)];
        Assert.Equal(versions.Distinct().Count(), versions.Length);
    }

    [Fact]
    public void ClassNames_MatchTheirVersion()
    {
        foreach (var (version, name) in Migrations)
            Assert.StartsWith($"M{version:000}_", name);
    }
}
