using FluentMigrator;

namespace StateBallot.Staging.Migrations;

/// <summary>
/// The version of the code that made a capture or pass, from &lt;Version&gt; in Directory.Build.props.
/// GitSha stays beside it and says which commit that version was built from. Null for rows stored
/// before this migration, and for builds with no version.
/// </summary>
[Migration(8, "Build version")]
public sealed class M008_BuildVersion : Migration
{
    public override void Up()
    {
        Alter.Table("Captures").AddColumn("Version").AsAnsiString(50).Nullable();
        Alter.Table("CollectionPasses").AddColumn("Version").AsAnsiString(50).Nullable();
    }

    public override void Down()
    {
        Delete.Column("Version").FromTable("Captures");
        Delete.Column("Version").FromTable("CollectionPasses");
    }
}
