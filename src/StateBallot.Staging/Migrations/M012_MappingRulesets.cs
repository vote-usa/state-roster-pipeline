using System.Data;
using FluentMigrator;

namespace StateBallot.Staging.Migrations;

/// <summary>
/// Mappings reference rulesets instead of holding their own transform steps. A ruleset that a mapping
/// references cannot be deleted (no cascade on RulesetId).
/// </summary>
[Migration(12, "Mappings reference rulesets")]
public sealed class M012_MappingRulesets : Migration
{
    public override void Up()
    {
        Delete.Table("TransformStepParams");
        Delete.Table("TransformSteps");

        Create.Table("TargetMappingRulesets")
            .WithColumn("TargetMappingRulesetId").AsInt32().NotNullable().PrimaryKey().Identity()
            .WithColumn("TargetMappingId").AsInt32().NotNullable()
            .WithColumn("RulesetId").AsInt32().NotNullable()
            .WithColumn("Position").AsInt32().NotNullable();

        Create.ForeignKey("fk_targetmappingrulesets_target")
            .FromTable("TargetMappingRulesets").ForeignColumn("TargetMappingId")
            .ToTable("TargetMappings").PrimaryColumn("TargetMappingId")
            .OnDelete(Rule.Cascade);

        Create.ForeignKey("fk_targetmappingrulesets_ruleset")
            .FromTable("TargetMappingRulesets").ForeignColumn("RulesetId")
            .ToTable("Rulesets").PrimaryColumn("RulesetId");

        Create.Index("ux_targetmappingrulesets_slot").OnTable("TargetMappingRulesets")
            .OnColumn("TargetMappingId").Ascending()
            .OnColumn("Position").Ascending()
            .WithOptions().Unique();

        Create.Table("MappingInputRulesets")
            .WithColumn("MappingInputRulesetId").AsInt32().NotNullable().PrimaryKey().Identity()
            .WithColumn("MappingInputId").AsInt32().NotNullable()
            .WithColumn("RulesetId").AsInt32().NotNullable()
            .WithColumn("Position").AsInt32().NotNullable();

        Create.ForeignKey("fk_mappinginputrulesets_input")
            .FromTable("MappingInputRulesets").ForeignColumn("MappingInputId")
            .ToTable("MappingInputs").PrimaryColumn("MappingInputId")
            .OnDelete(Rule.Cascade);

        Create.ForeignKey("fk_mappinginputrulesets_ruleset")
            .FromTable("MappingInputRulesets").ForeignColumn("RulesetId")
            .ToTable("Rulesets").PrimaryColumn("RulesetId");

        Create.Index("ux_mappinginputrulesets_slot").OnTable("MappingInputRulesets")
            .OnColumn("MappingInputId").Ascending()
            .OnColumn("Position").Ascending()
            .WithOptions().Unique();
    }

    public override void Down()
    {
        Delete.Table("MappingInputRulesets");
        Delete.Table("TargetMappingRulesets");

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
}
