using FluentMigrator;

namespace StateBallot.Staging.Migrations;

/// <summary>
/// The candidate's filing status as the source publishes it ("Active", "Withdrawn - 02/19/2026",
/// "Seeking the Nomination"). The vocabulary differs per source and is stored raw. Null when the
/// source does not publish one.
/// </summary>
[Migration(4, "Candidate filing status")]
public sealed class M004_CandidateStatus : Migration
{
    public override void Up() =>
        Alter.Table("RunCandidates").AddColumn("Status").AsAnsiString(255).Nullable();

    public override void Down() =>
        Delete.Column("Status").FromTable("RunCandidates");
}
