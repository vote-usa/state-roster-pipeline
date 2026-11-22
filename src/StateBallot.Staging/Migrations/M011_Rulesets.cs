using System.Data;
using FluentMigrator;

namespace StateBallot.Staging.Migrations;

[Migration(11, "Transform rulesets")]
public sealed class M011_Rulesets : Migration
{
    public override void Up()
    {
        Create.Table("Rulesets")
            .WithColumn("RulesetId").AsInt32().NotNullable().PrimaryKey().Identity()
            .WithColumn("Name").AsAnsiString(100).NotNullable()
            .WithColumn("Description").AsCustom("TEXT").Nullable()
            .WithColumn("IsActive").AsBoolean().NotNullable().WithDefaultValue(true)
            .WithColumn("CreatedAt").AsDateTime().NotNullable()
            .WithColumn("UpdatedAt").AsDateTime().NotNullable();

        Create.Index("ux_rulesets_name").OnTable("Rulesets")
            .OnColumn("Name").Ascending()
            .WithOptions().Unique();

        Create.Table("RulesetSteps")
            .WithColumn("RulesetStepId").AsInt32().NotNullable().PrimaryKey().Identity()
            .WithColumn("RulesetId").AsInt32().NotNullable()
            .WithColumn("Kind").AsAnsiString(32).NotNullable()
            .WithColumn("Position").AsInt32().NotNullable();

        Create.ForeignKey("fk_rulesetsteps_ruleset")
            .FromTable("RulesetSteps").ForeignColumn("RulesetId")
            .ToTable("Rulesets").PrimaryColumn("RulesetId")
            .OnDelete(Rule.Cascade);

        Create.Index("ux_rulesetsteps_slot").OnTable("RulesetSteps")
            .OnColumn("RulesetId").Ascending()
            .OnColumn("Position").Ascending()
            .WithOptions().Unique();

        Create.Table("RulesetStepParams")
            .WithColumn("RulesetStepParamId").AsInt32().NotNullable().PrimaryKey().Identity()
            .WithColumn("RulesetStepId").AsInt32().NotNullable()
            .WithColumn("Name").AsAnsiString(32).NotNullable()
            .WithColumn("Value").AsAnsiString(1024).NotNullable();

        Create.ForeignKey("fk_rulesetstepparams_step")
            .FromTable("RulesetStepParams").ForeignColumn("RulesetStepId")
            .ToTable("RulesetSteps").PrimaryColumn("RulesetStepId")
            .OnDelete(Rule.Cascade);

        Create.Index("ux_rulesetstepparams_name").OnTable("RulesetStepParams")
            .OnColumn("RulesetStepId").Ascending()
            .OnColumn("Name").Ascending()
            .WithOptions().Unique();
    }

    public override void Down()
    {
        Delete.Table("RulesetStepParams");
        Delete.Table("RulesetSteps");
        Delete.Table("Rulesets");
    }
}
