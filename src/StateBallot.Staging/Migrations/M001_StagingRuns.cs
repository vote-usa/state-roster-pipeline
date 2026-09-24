using FluentMigrator;

namespace StateBallot.Staging.Migrations;

/// <summary>
/// Staging store for collector output. The unit of record is one election, not one state.
///
/// A CLI invocation collects a whole state and year in one fetch, because that is how the
/// sources publish. That fetch is a CollectionPass. It fans out into one Runs row per
/// election, which is what gets reviewed, resolved and loaded. A run has exactly one
/// election, so RunCandidates and RunMeasures hang off RunId alone. State-level artifacts
/// that belong to no election (county directory, statewide measures with no ballot yet)
/// hang off the pass.
///
/// AsAnsiString is deliberate: AsString maps to NVARCHAR, which MySQL creates as utf8mb3.
/// </summary>
[Migration(1, "Staging runs")]
public sealed class M001_StagingRuns : Migration
{
    public override void Up()
    {
        Create.Table("CollectionPasses")
            .WithColumn("PassId").AsInt32().NotNullable().PrimaryKey().Identity()
            .WithColumn("StateCode").AsAnsiString(2).NotNullable()
            .WithColumn("Year").AsInt32().NotNullable()
            // queued | running | succeeded | failed
            .WithColumn("Status").AsAnsiString(16).NotNullable().WithDefaultValue("queued")
            // cli | web
            .WithColumn("Source").AsAnsiString(8).NotNullable().WithDefaultValue("cli")
            .WithColumn("RequestedBy").AsAnsiString(100).NotNullable().WithDefaultValue("")
            .WithColumn("RequestedAt").AsDateTime().NotNullable()
            .WithColumn("StartedAt").AsDateTime().Nullable()
            .WithColumn("FinishedAt").AsDateTime().Nullable()
            .WithColumn("CancelRequested").AsBoolean().NotNullable().WithDefaultValue(0)
            // set when the invocation targeted one election
            .WithColumn("ElectionFilter").AsDate().Nullable()
            .WithColumn("CliArgs").AsCustom("TEXT").Nullable()
            .WithColumn("GitSha").AsAnsiString(40).Nullable()
            .WithColumn("Wayback").AsAnsiString(14).Nullable()
            // elections this pass produced runs for
            .WithColumn("RunCount").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("CandidateCount").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("MeasureCount").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("CountyBallotCount").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("CountyDirectoryCount").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("ProposedMeasureCount").AsInt32().NotNullable().WithDefaultValue(0)
            // rows whose election could not be identified
            .WithColumn("UnassignedRowCount").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("GapCount").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("GapsJson").AsCustom("JSON").Nullable()
            // provenance per data group, was sources.json
            .WithColumn("SourcesJson").AsCustom("JSON").Nullable()
            .WithColumn("Summary").AsCustom("TEXT").Nullable()
            .WithColumn("LogText").AsCustom("MEDIUMTEXT").Nullable()
            .WithColumn("ErrorText").AsCustom("TEXT").Nullable();

        Create.Index("ix_passes_state_requested").OnTable("CollectionPasses")
            .OnColumn("StateCode").Ascending()
            .OnColumn("RequestedAt").Ascending();
        Create.Index("ix_passes_status").OnTable("CollectionPasses")
            .OnColumn("Status").Ascending();

        Create.Table("Runs")
            .WithColumn("RunId").AsInt32().NotNullable().PrimaryKey().Identity()
            .WithColumn("PassId").AsInt32().NotNullable()
            .WithColumn("StateCode").AsAnsiString(2).NotNullable()
            .WithColumn("Year").AsInt32().NotNullable()
            .WithColumn("ElectionDate").AsDate().NotNullable()
            .WithColumn("ElectionType").AsAnsiString(50).NotNullable().WithDefaultValue("")
            .WithColumn("ElectionName").AsAnsiString(255).NotNullable().WithDefaultValue("")
            .WithColumn("SourceElectionId").AsAnsiString(50).NotNullable().WithDefaultValue("")
            .WithColumn("Jurisdiction").AsAnsiString(200).NotNullable().WithDefaultValue("")
            .WithColumn("OcdDivisionId").AsAnsiString(255).Nullable()
            .WithColumn("SourceUrl").AsCustom("TEXT").Nullable()
            // succeeded | pending
            .WithColumn("Status").AsAnsiString(16).NotNullable().WithDefaultValue("succeeded")
            // source has not published ballot data yet
            .WithColumn("IsPending").AsBoolean().NotNullable().WithDefaultValue(0)
            .WithColumn("CandidateCount").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("MeasureCount").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("CountyBallotCount").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("CreatedAt").AsDateTime().NotNullable()
            // VoteUSA key once resolved
            .WithColumn("ElectionKey").AsAnsiString(18).Nullable()
            // resolved | new | unresolved
            .WithColumn("ResolveStatus").AsAnsiString(16).Nullable()
            .WithColumn("ResolveReason").AsAnsiString(100).Nullable();

        Create.Index("ix_runs_pass").OnTable("Runs")
            .OnColumn("PassId").Ascending();
        Create.Index("ix_runs_election").OnTable("Runs")
            .OnColumn("StateCode").Ascending()
            .OnColumn("ElectionDate").Ascending();
        Create.Index("ix_runs_status").OnTable("Runs")
            .OnColumn("Status").Ascending();

        // SourceElectionId is the source system's own id for the election a row was
        // collected for. A date plus a type cannot always identify one: TX ran two special
        // elections on 2026-11-03, both typed Special. The collectors know the election
        // when they build each row, so the id is carried through and the matcher prefers it.
        Create.Table("RunCandidates")
            .WithColumn("RunCandidateId").AsInt32().NotNullable().PrimaryKey().Identity()
            .WithColumn("RunId").AsInt32().NotNullable()
            .WithColumn("StateCode").AsAnsiString(2).NotNullable()
            .WithColumn("ElectionDate").AsDate().NotNullable()
            .WithColumn("ElectionType").AsAnsiString(50).NotNullable().WithDefaultValue("")
            .WithColumn("Office").AsAnsiString(255).NotNullable().WithDefaultValue("")
            .WithColumn("District").AsAnsiString(255).Nullable()
            .WithColumn("County").AsAnsiString(255).Nullable()
            .WithColumn("OcdDivisionId").AsAnsiString(255).Nullable()
            .WithColumn("CandidateName").AsAnsiString(255).NotNullable().WithDefaultValue("")
            .WithColumn("Party").AsAnsiString(100).Nullable()
            .WithColumn("Incumbent").AsBoolean().Nullable()
            .WithColumn("SourceUrl").AsCustom("TEXT").Nullable()
            .WithColumn("SourceCandidateId").AsAnsiString(100).Nullable()
            .WithColumn("SourceElectionId").AsAnsiString(50).Nullable()
            .WithColumn("FilingDate").AsAnsiString(30).Nullable()
            .WithColumn("Email").AsAnsiString(255).Nullable()
            .WithColumn("Phone").AsAnsiString(50).Nullable()
            .WithColumn("CampaignPhone").AsAnsiString(50).Nullable()
            .WithColumn("Website").AsCustom("TEXT").Nullable()
            .WithColumn("Occupation").AsAnsiString(255).Nullable()
            .WithColumn("MailingAddressLine").AsAnsiString(255).Nullable()
            .WithColumn("MailingCity").AsAnsiString(100).Nullable()
            .WithColumn("MailingState").AsAnsiString(20).Nullable()
            .WithColumn("MailingZip").AsAnsiString(20).Nullable()
            .WithColumn("ResidentialCity").AsAnsiString(100).Nullable()
            .WithColumn("ResidentialCounty").AsAnsiString(100).Nullable()
            .WithColumn("SourceOfficeId").AsAnsiString(100).Nullable()
            .WithColumn("SourceOfficeType").AsAnsiString(100).Nullable()
            .WithColumn("LocalJurisdiction").AsAnsiString(255).Nullable()
            .WithColumn("FirstName").AsAnsiString(100).Nullable()
            .WithColumn("MiddleName").AsAnsiString(100).Nullable()
            .WithColumn("LastName").AsAnsiString(100).Nullable()
            .WithColumn("Suffix").AsAnsiString(20).Nullable()
            .WithColumn("OfficeKey").AsAnsiString(150).Nullable()
            .WithColumn("PoliticianKey").AsAnsiString(50).Nullable()
            .WithColumn("OfficeResolveStatus").AsAnsiString(16).Nullable()
            .WithColumn("PoliticianResolveStatus").AsAnsiString(16).Nullable()
            .WithColumn("ResolveReason").AsAnsiString(100).Nullable();

        Create.Index("ix_runcandidates_run").OnTable("RunCandidates")
            .OnColumn("RunId").Ascending();

        Create.Table("RunMeasures")
            .WithColumn("RunMeasureId").AsInt32().NotNullable().PrimaryKey().Identity()
            .WithColumn("RunId").AsInt32().NotNullable()
            .WithColumn("StateCode").AsAnsiString(2).NotNullable()
            .WithColumn("ElectionDate").AsDate().NotNullable()
            .WithColumn("SourceElectionId").AsAnsiString(50).Nullable()
            .WithColumn("MeasureId").AsAnsiString(100).NotNullable().WithDefaultValue("")
            .WithColumn("Title").AsCustom("TEXT").Nullable()
            .WithColumn("Summary").AsCustom("TEXT").Nullable()
            .WithColumn("FullTextUrl").AsCustom("TEXT").Nullable()
            .WithColumn("Jurisdiction").AsAnsiString(255).NotNullable().WithDefaultValue("")
            .WithColumn("County").AsAnsiString(255).Nullable()
            .WithColumn("OcdDivisionId").AsAnsiString(255).Nullable()
            .WithColumn("SourceUrl").AsCustom("TEXT").Nullable()
            .WithColumn("IsStatewideProposed").AsBoolean().NotNullable().WithDefaultValue(0)
            .WithColumn("ReferendumKey").AsAnsiString(150).Nullable()
            .WithColumn("ResolveStatus").AsAnsiString(16).Nullable()
            .WithColumn("ResolveReason").AsAnsiString(100).Nullable();

        Create.Index("ix_runmeasures_run").OnTable("RunMeasures")
            .OnColumn("RunId").Ascending();

        // One county's ballot for one election, as the source published it. The entries
        // overlap RunCandidates and RunMeasures but are kept verbatim: they are what that
        // county's voters actually see, including local races the statewide feed does not
        // attribute.
        Create.Table("RunCountyBallots")
            .WithColumn("RunCountyBallotId").AsInt32().NotNullable().PrimaryKey().Identity()
            .WithColumn("RunId").AsInt32().NotNullable()
            .WithColumn("StateCode").AsAnsiString(2).NotNullable()
            .WithColumn("County").AsAnsiString(255).NotNullable()
            .WithColumn("OcdDivisionId").AsAnsiString(255).Nullable()
            .WithColumn("SourceUrl").AsCustom("TEXT").Nullable()
            .WithColumn("CandidateCount").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("MeasureCount").AsInt32().NotNullable().WithDefaultValue(0);

        Create.Index("ix_countyballots_run").OnTable("RunCountyBallots")
            .OnColumn("RunId").Ascending();

        Create.Table("RunCountyBallotCandidates")
            .WithColumn("Id").AsInt32().NotNullable().PrimaryKey().Identity()
            .WithColumn("RunCountyBallotId").AsInt32().NotNullable()
            .WithColumn("RunId").AsInt32().NotNullable()
            .WithColumn("Office").AsAnsiString(255).NotNullable().WithDefaultValue("")
            .WithColumn("District").AsAnsiString(255).Nullable()
            .WithColumn("OcdDivisionId").AsAnsiString(255).Nullable()
            .WithColumn("CandidateName").AsAnsiString(255).NotNullable().WithDefaultValue("")
            .WithColumn("Party").AsAnsiString(100).Nullable()
            .WithColumn("SourceOfficeId").AsAnsiString(100).Nullable()
            .WithColumn("SourceOfficeType").AsAnsiString(100).Nullable()
            .WithColumn("LocalJurisdiction").AsAnsiString(255).Nullable();

        Create.Index("ix_cbcandidates_ballot").OnTable("RunCountyBallotCandidates")
            .OnColumn("RunCountyBallotId").Ascending();
        Create.Index("ix_cbcandidates_run").OnTable("RunCountyBallotCandidates")
            .OnColumn("RunId").Ascending();

        Create.Table("RunCountyBallotMeasures")
            .WithColumn("Id").AsInt32().NotNullable().PrimaryKey().Identity()
            .WithColumn("RunCountyBallotId").AsInt32().NotNullable()
            .WithColumn("RunId").AsInt32().NotNullable()
            .WithColumn("MeasureId").AsAnsiString(100).NotNullable().WithDefaultValue("")
            .WithColumn("Title").AsCustom("TEXT").Nullable()
            .WithColumn("Summary").AsCustom("TEXT").Nullable()
            .WithColumn("Jurisdiction").AsAnsiString(255).NotNullable().WithDefaultValue("")
            .WithColumn("OcdDivisionId").AsAnsiString(255).Nullable();

        Create.Index("ix_cbmeasures_ballot").OnTable("RunCountyBallotMeasures")
            .OnColumn("RunCountyBallotId").Ascending();
        Create.Index("ix_cbmeasures_run").OnTable("RunCountyBallotMeasures")
            .OnColumn("RunId").Ascending();

        // State-level, belongs to no election.
        Create.Table("PassCountyDirectory")
            .WithColumn("Id").AsInt32().NotNullable().PrimaryKey().Identity()
            .WithColumn("PassId").AsInt32().NotNullable()
            .WithColumn("StateCode").AsAnsiString(2).NotNullable()
            .WithColumn("CountyName").AsAnsiString(255).NotNullable()
            .WithColumn("CountyFips").AsAnsiString(5).Nullable()
            .WithColumn("OcdDivisionId").AsAnsiString(255).Nullable()
            .WithColumn("ElectionsOfficeUrl").AsCustom("TEXT").Nullable()
            .WithColumn("Address").AsCustom("TEXT").Nullable()
            .WithColumn("Phone").AsAnsiString(100).Nullable();

        Create.Index("ix_countydirectory_pass").OnTable("PassCountyDirectory")
            .OnColumn("PassId").Ascending();

        // Statewide measures with no election date yet (not certified to a ballot).
        Create.Table("PassProposedMeasures")
            .WithColumn("Id").AsInt32().NotNullable().PrimaryKey().Identity()
            .WithColumn("PassId").AsInt32().NotNullable()
            .WithColumn("StateCode").AsAnsiString(2).NotNullable()
            .WithColumn("MeasureId").AsAnsiString(100).NotNullable().WithDefaultValue("")
            .WithColumn("Title").AsCustom("TEXT").Nullable()
            .WithColumn("Summary").AsCustom("TEXT").Nullable()
            .WithColumn("FullTextUrl").AsCustom("TEXT").Nullable()
            .WithColumn("Jurisdiction").AsAnsiString(255).NotNullable().WithDefaultValue("")
            .WithColumn("County").AsAnsiString(255).Nullable()
            .WithColumn("OcdDivisionId").AsAnsiString(255).Nullable()
            .WithColumn("SourceUrl").AsCustom("TEXT").Nullable();

        Create.Index("ix_proposedmeasures_pass").OnTable("PassProposedMeasures")
            .OnColumn("PassId").Ascending();

        // Rows the collector produced whose election could not be identified. Never dropped
        // silently: they are recorded here with the reason and counted on the pass.
        Create.Table("PassUnassignedRows")
            .WithColumn("Id").AsInt32().NotNullable().PrimaryKey().Identity()
            .WithColumn("PassId").AsInt32().NotNullable()
            // candidate | measure | county_ballot
            .WithColumn("RowType").AsAnsiString(16).NotNullable()
            .WithColumn("ElectionDate").AsAnsiString(30).Nullable()
            .WithColumn("ElectionType").AsAnsiString(50).Nullable()
            // no_matching_election | ambiguous_election
            .WithColumn("Reason").AsAnsiString(100).NotNullable()
            .WithColumn("RowJson").AsCustom("JSON").Nullable();

        Create.Index("ix_unassigned_pass").OnTable("PassUnassignedRows")
            .OnColumn("PassId").Ascending();
    }

    public override void Down()
    {
        Delete.Table("PassUnassignedRows");
        Delete.Table("PassProposedMeasures");
        Delete.Table("PassCountyDirectory");
        Delete.Table("RunCountyBallotMeasures");
        Delete.Table("RunCountyBallotCandidates");
        Delete.Table("RunCountyBallots");
        Delete.Table("RunMeasures");
        Delete.Table("RunCandidates");
        Delete.Table("Runs");
        Delete.Table("CollectionPasses");
    }
}
