using System.Data;
using FluentMigrator;

namespace StateBallot.Staging.Migrations;

[Migration(6, "Column mappings")]
public sealed class M006_ColumnMappings : Migration
{
    public override void Up()
    {
        // AsAnsiString: AsString maps to NVARCHAR, which MySQL creates as utf8mb3.
        Create.Table("MappingGroups")
            .WithColumn("MappingGroupId").AsInt32().NotNullable().PrimaryKey().Identity()
            .WithColumn("Name").AsAnsiString(100).NotNullable()
            .WithColumn("StateCode").AsAnsiString(2).NotNullable()
            .WithColumn("Year").AsInt32().Nullable()
            .WithColumn("ElectionType").AsAnsiString(32).Nullable()
            .WithColumn("Party").AsAnsiString(32).Nullable()
            .WithColumn("Description").AsCustom("TEXT").Nullable()
            .WithColumn("CreatedAt").AsDateTime().NotNullable()
            .WithColumn("UpdatedAt").AsDateTime().NotNullable();

        Create.Index("ix_mappinggroups_facets").OnTable("MappingGroups")
            .OnColumn("StateCode").Ascending()
            .OnColumn("Year").Ascending()
            .OnColumn("ElectionType").Ascending()
            .OnColumn("Party").Ascending();

        Create.Table("TargetMappings")
            .WithColumn("TargetMappingId").AsInt32().NotNullable().PrimaryKey().Identity()
            .WithColumn("MappingGroupId").AsInt32().NotNullable()
            .WithColumn("TargetColumn").AsAnsiString(64).NotNullable();

        Create.ForeignKey("fk_targetmappings_group")
            .FromTable("TargetMappings").ForeignColumn("MappingGroupId")
            .ToTable("MappingGroups").PrimaryColumn("MappingGroupId")
            .OnDelete(Rule.Cascade);

        Create.Index("ux_targetmappings_target").OnTable("TargetMappings")
            .OnColumn("MappingGroupId").Ascending()
            .OnColumn("TargetColumn").Ascending()
            .WithOptions().Unique();

        Create.Table("MappingInputs")
            .WithColumn("MappingInputId").AsInt32().NotNullable().PrimaryKey().Identity()
            .WithColumn("TargetMappingId").AsInt32().NotNullable()
            .WithColumn("Kind").AsAnsiString(10).NotNullable()
            .WithColumn("SourceColumn").AsAnsiString(255).Nullable()
            .WithColumn("ManualValue").AsAnsiString(1024).Nullable()
            .WithColumn("Position").AsInt32().NotNullable();

        Create.ForeignKey("fk_mappinginputs_target")
            .FromTable("MappingInputs").ForeignColumn("TargetMappingId")
            .ToTable("TargetMappings").PrimaryColumn("TargetMappingId")
            .OnDelete(Rule.Cascade);

        Create.Index("ux_mappinginputs_slot").OnTable("MappingInputs")
            .OnColumn("TargetMappingId").Ascending()
            .OnColumn("Position").Ascending()
            .WithOptions().Unique();

        Create.Index("ix_mappinginputs_source").OnTable("MappingInputs")
            .OnColumn("SourceColumn").Ascending();

        // A step is target-scoped (combine/finish) when MappingInputId is null, or input-scoped
        // (runs on one input before combining) when it names an input.
        Create.Table("TransformSteps")
            .WithColumn("TransformStepId").AsInt32().NotNullable().PrimaryKey().Identity()
            .WithColumn("TargetMappingId").AsInt32().NotNullable()
            .WithColumn("MappingInputId").AsInt32().Nullable()
            .WithColumn("Kind").AsAnsiString(32).NotNullable()
            .WithColumn("Position").AsInt32().NotNullable();

        Create.ForeignKey("fk_transformsteps_target")
            .FromTable("TransformSteps").ForeignColumn("TargetMappingId")
            .ToTable("TargetMappings").PrimaryColumn("TargetMappingId")
            .OnDelete(Rule.Cascade);

        Create.ForeignKey("fk_transformsteps_input")
            .FromTable("TransformSteps").ForeignColumn("MappingInputId")
            .ToTable("MappingInputs").PrimaryColumn("MappingInputId")
            .OnDelete(Rule.Cascade);

        Create.Index("ix_transformsteps_target").OnTable("TransformSteps")
            .OnColumn("TargetMappingId").Ascending();
        Create.Index("ix_transformsteps_input").OnTable("TransformSteps")
            .OnColumn("MappingInputId").Ascending();

        Create.Table("TransformStepParams")
            .WithColumn("TransformStepParamId").AsInt32().NotNullable().PrimaryKey().Identity()
            .WithColumn("TransformStepId").AsInt32().NotNullable()
            .WithColumn("Name").AsAnsiString(32).NotNullable()
            .WithColumn("Value").AsAnsiString(1024).NotNullable();

        Create.ForeignKey("fk_transformstepparams_step")
            .FromTable("TransformStepParams").ForeignColumn("TransformStepId")
            .ToTable("TransformSteps").PrimaryColumn("TransformStepId")
            .OnDelete(Rule.Cascade);

        Create.Index("ux_transformstepparams_name").OnTable("TransformStepParams")
            .OnColumn("TransformStepId").Ascending()
            .OnColumn("Name").Ascending()
            .WithOptions().Unique();
    }

    public override void Down()
    {
        Delete.Table("TransformStepParams");
        Delete.Table("TransformSteps");
        Delete.Table("MappingInputs");
        Delete.Table("TargetMappings");
        Delete.Table("MappingGroups");
    }
}
