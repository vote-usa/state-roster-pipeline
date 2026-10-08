using FluentMigrator;

namespace StateBallot.Staging.Migrations;

/// <summary>
/// Run requests made from the console. A request cannot wait on a run, so the API stores a
/// queued job and a worker claims it, one at a time. A job says what to run: a capture, a
/// normalization of an existing capture, or both. What it produced is in Captures,
/// CollectionPasses and Runs as for a CLI run, and the job only points at them. A CLI run
/// has no job.
/// </summary>
[Migration(5, "Console jobs")]
public sealed class M005_Jobs : Migration
{
    public override void Up()
    {
        Create.Table("Jobs")
            .WithColumn("JobId").AsInt32().NotNullable().PrimaryKey().Identity()
            .WithColumn("StateCode").AsAnsiString(2).NotNullable()
            .WithColumn("Year").AsInt32().NotNullable()
            // capture | normalize | both
            .WithColumn("Kind").AsAnsiString(16).NotNullable()
            // the capture to read, for normalize
            .WithColumn("NormalizeCaptureId").AsInt32().Nullable()
            .WithColumn("ElectionFilter").AsDate().Nullable()
            // queued | running | succeeded | failed
            .WithColumn("Status").AsAnsiString(16).NotNullable().WithDefaultValue("queued")
            .WithColumn("RequestedBy").AsAnsiString(100).NotNullable().WithDefaultValue("")
            .WithColumn("RequestedAt").AsDateTime().NotNullable()
            .WithColumn("StartedAt").AsDateTime().Nullable()
            .WithColumn("FinishedAt").AsDateTime().Nullable()
            // what the job produced or read, set when it finishes
            .WithColumn("CaptureId").AsInt32().Nullable()
            .WithColumn("PassId").AsInt32().Nullable()
            .WithColumn("ErrorText").AsCustom("TEXT").Nullable();

        Create.Index("ix_jobs_status").OnTable("Jobs")
            .OnColumn("Status").Ascending();
    }

    public override void Down() => Delete.Table("Jobs");
}
