using FluentMigrator;

namespace StateBallot.Staging.Migrations;

/// <summary>
/// Captures. A run is two stages: a capture fetches and saves every payload, then a pass
/// normalizes the saved capture into runs. A capture can be normalized many times, so it is
/// its own record and each pass points at the capture it read. Payloads stay on disk under
/// data/raw/&lt;xx&gt;/&lt;capture-id&gt;/ because they are large. CaptureFetches keeps one row per
/// fetch with its role, keys and payload hash, so a stored row can be tied to an exact
/// payload even after retention prunes the files.
/// </summary>
[Migration(2, "Captures")]
public sealed class M002_RawCapture : Migration
{
    public override void Up()
    {
        Create.Table("Captures")
            .WithColumn("CaptureId").AsInt32().NotNullable().PrimaryKey().Identity()
            .WithColumn("StateCode").AsAnsiString(2).NotNullable()
            .WithColumn("Year").AsInt32().NotNullable()
            // running | succeeded | failed
            .WithColumn("Status").AsAnsiString(16).NotNullable().WithDefaultValue("running")
            // cli | web
            .WithColumn("Source").AsAnsiString(8).NotNullable().WithDefaultValue("cli")
            .WithColumn("RequestedBy").AsAnsiString(100).NotNullable().WithDefaultValue("")
            .WithColumn("StartedAt").AsDateTime().NotNullable()
            .WithColumn("FinishedAt").AsDateTime().Nullable()
            .WithColumn("CliArgs").AsCustom("TEXT").Nullable()
            .WithColumn("GitSha").AsAnsiString(40).Nullable()
            .WithColumn("Wayback").AsAnsiString(14).Nullable()
            // relative to the pipeline data root, e.g. raw/wv/12
            .WithColumn("RawDir").AsAnsiString(255).Nullable()
            .WithColumn("FetchCount").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("RawBytes").AsInt64().NotNullable().WithDefaultValue(0)
            .WithColumn("FetchLogSha256").AsAnsiString(64).Nullable()
            .WithColumn("LogText").AsCustom("MEDIUMTEXT").Nullable()
            .WithColumn("ErrorText").AsCustom("TEXT").Nullable();

        Create.Index("ix_captures_state_started").OnTable("Captures")
            .OnColumn("StateCode").Ascending()
            .OnColumn("StartedAt").Ascending();

        Create.Table("CaptureFetches")
            .WithColumn("Id").AsInt32().NotNullable().PrimaryKey().Identity()
            .WithColumn("CaptureId").AsInt32().NotNullable()
            .WithColumn("Seq").AsInt32().NotNullable()
            .WithColumn("FetchedAt").AsDateTime().NotNullable()
            // what the payload is to the collector, e.g. county-guide
            .WithColumn("Role").AsAnsiString(64).Nullable()
            // which one of the role, e.g. {"county": "01", "election": "123"}
            .WithColumn("KeysJson").AsCustom("JSON").Nullable()
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

        Create.Index("ux_capture_fetches_seq").OnTable("CaptureFetches")
            .OnColumn("CaptureId").Ascending()
            .OnColumn("Seq").Ascending()
            .WithOptions().Unique();
        Create.Index("ix_capture_fetches_payload").OnTable("CaptureFetches")
            .OnColumn("PayloadSha256").Ascending();

        // null only for passes stored before captures existed
        Alter.Table("CollectionPasses")
            .AddColumn("CaptureId").AsInt32().Nullable();
        Create.Index("ix_passes_capture").OnTable("CollectionPasses")
            .OnColumn("CaptureId").Ascending();
    }

    public override void Down()
    {
        Delete.Index("ix_passes_capture").OnTable("CollectionPasses");
        Delete.Column("CaptureId").FromTable("CollectionPasses");
        Delete.Table("CaptureFetches");
        Delete.Table("Captures");
    }
}
