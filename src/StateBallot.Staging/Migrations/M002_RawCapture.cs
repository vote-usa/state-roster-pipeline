using FluentMigrator;

namespace StateBallot.Staging.Migrations;

/// <summary>
/// Raw capture. Payloads stay on disk under data/raw/&lt;xx&gt;/&lt;capture-id&gt;/ because they
/// are large. The database keeps one PassFetches row per fetch with the payload hash, so a
/// stored row can be tied to an exact payload even after retention prunes the files.
/// </summary>
[Migration(2, "Raw capture")]
public sealed class M002_RawCapture : Migration
{
    public override void Up()
    {
        Alter.Table("CollectionPasses")
            // relative to the pipeline data root, e.g. raw/wv/12
            .AddColumn("RawDir").AsAnsiString(255).Nullable()
            .AddColumn("FetchCount").AsInt32().NotNullable().WithDefaultValue(0)
            .AddColumn("RawBytes").AsInt64().NotNullable().WithDefaultValue(0)
            .AddColumn("FetchLogSha256").AsAnsiString(64).Nullable()
            // set when this pass re-ran a saved capture instead of the network
            .AddColumn("ReplayOfPassId").AsInt32().Nullable();

        Create.Table("PassFetches")
            .WithColumn("Id").AsInt32().NotNullable().PrimaryKey().Identity()
            .WithColumn("PassId").AsInt32().NotNullable()
            .WithColumn("Seq").AsInt32().NotNullable()
            .WithColumn("FetchedAt").AsDateTime().NotNullable()
            .WithColumn("Method").AsAnsiString(8).NotNullable()
            .WithColumn("Url").AsCustom("TEXT").NotNullable()
            .WithColumn("RequestSha256").AsAnsiString(64).Nullable()
            // null when no response arrived
            .WithColumn("Status").AsInt32().Nullable()
            .WithColumn("ContentType").AsAnsiString(255).Nullable()
            .WithColumn("Bytes").AsInt64().Nullable()
            .WithColumn("PayloadSha256").AsAnsiString(64).Nullable()
            .WithColumn("FileName").AsAnsiString(255).Nullable()
            .WithColumn("DurationMs").AsInt64().NotNullable()
            .WithColumn("Error").AsCustom("TEXT").Nullable();

        Create.Index("ux_fetches_pass_seq").OnTable("PassFetches")
            .OnColumn("PassId").Ascending()
            .OnColumn("Seq").Ascending()
            .WithOptions().Unique();
        Create.Index("ix_fetches_payload").OnTable("PassFetches")
            .OnColumn("PayloadSha256").Ascending();
    }

    public override void Down()
    {
        Delete.Table("PassFetches");
        Delete.Column("RawDir").Column("FetchCount").Column("RawBytes")
            .Column("FetchLogSha256").Column("ReplayOfPassId")
            .FromTable("CollectionPasses");
    }
}
