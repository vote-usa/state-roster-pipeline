using FluentMigrator;

namespace StateBallot.Staging.Migrations;

/// <summary>
/// County on candidate and measure rows can be a list: a multi-county race or measure joins
/// every county name with "; ". WA 2026 has measures whose list runs to 356 characters, past
/// VARCHAR(255), which failed the whole pass. TEXT removes the cap. No index uses these
/// columns. RunCountyBallots.County always holds one county name and keeps VARCHAR(255).
/// </summary>
[Migration(3, "County lists as TEXT")]
public sealed class M003_CountyListText : Migration
{
    private static readonly string[] Tables = ["RunCandidates", "RunMeasures", "PassProposedMeasures"];

    public override void Up()
    {
        foreach (var table in Tables)
            Alter.Column("County").OnTable(table).AsCustom("TEXT").Nullable();
    }

    /// <remarks>Fails if any stored county list is longer than 255 characters.</remarks>
    public override void Down()
    {
        foreach (var table in Tables)
            Alter.Column("County").OnTable(table).AsAnsiString(255).Nullable();
    }
}
