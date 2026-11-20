using FluentMigrator;

namespace StateBallot.Staging.Migrations;

/// <summary>
/// URLs a person keeps for a state, or for counties and a city in it, edited from the console's
/// state page. They are for people to read; collectors keep taking their links from SourceLinks.
/// RegionUrls holds each URL as it is now. Every add, edit, delete and revert also writes a
/// RegionUrlVersions row, so the console can show a URL's history and put an old version back.
/// </summary>
[Migration(9, "Region URLs")]
public sealed class M009_RegionUrls : Migration
{
    public override void Up()
    {
        Create.Table("RegionUrls")
            .WithColumn("UrlId").AsInt32().NotNullable().PrimaryKey().Identity()
            .WithColumn("StateCode").AsAnsiString(2).NotNullable()
            // JSON array of county names, empty for a URL about the whole state
            .WithColumn("CountiesJson").AsCustom("JSON").NotNullable()
            .WithColumn("City").AsAnsiString(100).Nullable()
            .WithColumn("Url").AsCustom("TEXT").NotNullable()
            // official_landing | ballotpedia | candidate_data | measure_data
            .WithColumn("UrlType").AsAnsiString(32).NotNullable()
            // a deleted URL keeps its row and history, so it can be reverted
            .WithColumn("IsDeleted").AsBoolean().NotNullable().WithDefaultValue(0)
            .WithColumn("UpdatedAt").AsDateTime().NotNullable()
            .WithColumn("UpdatedBy").AsAnsiString(100).NotNullable();

        Create.Index("ix_region_urls_state").OnTable("RegionUrls")
            .OnColumn("StateCode").Ascending();

        Create.Table("RegionUrlVersions")
            .WithColumn("VersionId").AsInt32().NotNullable().PrimaryKey().Identity()
            .WithColumn("UrlId").AsInt32().NotNullable()
            // added | edited | reverted | deleted
            .WithColumn("Action").AsAnsiString(16).NotNullable()
            .WithColumn("StateCode").AsAnsiString(2).NotNullable()
            .WithColumn("CountiesJson").AsCustom("JSON").NotNullable()
            .WithColumn("City").AsAnsiString(100).Nullable()
            .WithColumn("Url").AsCustom("TEXT").NotNullable()
            .WithColumn("UrlType").AsAnsiString(32).NotNullable()
            .WithColumn("EditedAt").AsDateTime().NotNullable()
            .WithColumn("EditedBy").AsAnsiString(100).NotNullable();

        Create.Index("ix_region_url_versions_url").OnTable("RegionUrlVersions")
            .OnColumn("UrlId").Ascending();
        Create.Index("ix_region_url_versions_state").OnTable("RegionUrlVersions")
            .OnColumn("StateCode").Ascending();
    }

    public override void Down()
    {
        Delete.Table("RegionUrlVersions");
        Delete.Table("RegionUrls");
    }
}
