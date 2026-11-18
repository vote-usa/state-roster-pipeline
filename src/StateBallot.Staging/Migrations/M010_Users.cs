using FluentMigrator;

namespace StateBallot.Staging.Migrations;

/// <summary>
/// The people who can sign in to the run console. Every signed-in user can do everything, so there
/// are no roles. Users are created from the CLI (--add-user). PasswordHash is ASP.NET Core Identity's
/// PasswordHasher format, never the password itself. A sign-in after too many failures is refused
/// until LockedUntil.
/// </summary>
[Migration(10, "Users")]
public sealed class M010_Users : Migration
{
    public override void Up()
    {
        Create.Table("Users")
            .WithColumn("UserName").AsAnsiString(100).NotNullable().PrimaryKey()
            .WithColumn("PasswordHash").AsAnsiString(255).NotNullable()
            .WithColumn("IsDisabled").AsBoolean().NotNullable().WithDefaultValue(0)
            .WithColumn("FailedLogins").AsInt32().NotNullable().WithDefaultValue(0)
            .WithColumn("LockedUntil").AsDateTime().Nullable()
            .WithColumn("LastLoginAt").AsDateTime().Nullable()
            .WithColumn("CreatedAt").AsDateTime().NotNullable();
    }

    public override void Down()
    {
        Delete.Table("Users");
    }
}
