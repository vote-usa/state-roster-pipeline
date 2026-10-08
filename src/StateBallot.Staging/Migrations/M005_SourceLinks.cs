using FluentMigrator;

namespace StateBallot.Staging.Migrations;

/// <summary>
/// Source links and parameters, editable from the console. One row per (state, key, variant).
/// The rows are seeded from data/input/&lt;xx&gt;/source_links.json by <see cref="LinkStore"/>
/// after migrating, which inserts missing rows and never overwrites an edited one. Captures
/// keep a snapshot of the links they used, so a capture made after an edit can be explained
/// and normalized again with the same URLs.
/// </summary>
[Migration(5, "Source links")]
public sealed class M005_SourceLinks : Migration
{
    public override void Up()
    {
        Create.Table("SourceLinks")
            .WithColumn("SourceLinkId").AsInt32().NotNullable().PrimaryKey().Identity()
            .WithColumn("StateCode").AsAnsiString(2).NotNullable()
            // matches the fetch role the collector tags the download with
            .WithColumn("LinkKey").AsAnsiString(64).NotNullable()
            // "primary" / "general", or empty when the key has one URL
            .WithColumn("Variant").AsAnsiString(32).NotNullable().WithDefaultValue("")
            // may hold {year}, {electionId}, {countyCode}, {raceId}
            .WithColumn("UrlTemplate").AsCustom("TEXT").NotNullable()
            .WithColumn("Format").AsAnsiString(16).Nullable()
            // fetch | home | verification
            .WithColumn("Kind").AsAnsiString(16).NotNullable().WithDefaultValue("fetch")
            .WithColumn("Notes").AsCustom("TEXT").Nullable()
            .WithColumn("IsActive").AsBoolean().NotNullable().WithDefaultValue(1)
            .WithColumn("UpdatedAt").AsDateTime().NotNullable()
            // "seed" for rows the seeder wrote
            .WithColumn("UpdatedBy").AsAnsiString(100).NotNullable().WithDefaultValue("");

        Create.Index("ux_source_links_key").OnTable("SourceLinks")
            .OnColumn("StateCode").Ascending()
            .OnColumn("LinkKey").Ascending()
            .OnColumn("Variant").Ascending()
            .WithOptions().Unique();

        // hand-maintained values beside the links, e.g. election ids a source has no index for
        Create.Table("SourceParameters")
            .WithColumn("SourceParameterId").AsInt32().NotNullable().PrimaryKey().Identity()
            .WithColumn("StateCode").AsAnsiString(2).NotNullable()
            .WithColumn("ParamKey").AsAnsiString(64).NotNullable()
            .WithColumn("Variant").AsAnsiString(32).NotNullable().WithDefaultValue("")
            .WithColumn("Value").AsCustom("TEXT").NotNullable()
            .WithColumn("Notes").AsCustom("TEXT").Nullable()
            .WithColumn("UpdatedAt").AsDateTime().NotNullable()
            .WithColumn("UpdatedBy").AsAnsiString(100).NotNullable().WithDefaultValue("");

        Create.Index("ux_source_parameters_key").OnTable("SourceParameters")
            .OnColumn("StateCode").Ascending()
            .OnColumn("ParamKey").Ascending()
            .OnColumn("Variant").Ascending()
            .WithOptions().Unique();

        // the links and parameters a capture used; null for captures made before this migration
        Alter.Table("Captures").AddColumn("LinksJson").AsCustom("JSON").Nullable();
    }

    public override void Down()
    {
        Delete.Column("LinksJson").FromTable("Captures");
        Delete.Table("SourceParameters");
        Delete.Table("SourceLinks");
    }
}
