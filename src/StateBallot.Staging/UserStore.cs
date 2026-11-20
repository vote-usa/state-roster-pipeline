using Dapper;
using Microsoft.AspNetCore.Identity;

namespace StateBallot.Staging;

/// <summary>
/// The people who can sign in to the run console (Users). Passwords are kept only as ASP.NET Core
/// Identity PasswordHasher hashes. After MaxFailures wrong passwords in a row a user is locked out
/// for LockoutMinutes, and a correct password during the lockout is refused too. The count starts again
/// once a lockout is set.
/// </summary>
public sealed class UserStore
{
    public const int MaxFailures = 5;
    public const int LockoutMinutes = 15;
    public const int MinPasswordLength = 12;

    public enum SignIn { Ok, Invalid, Locked, Disabled }

    private static readonly PasswordHasher<string> Hasher = new();

    // Checked against when the user name is unknown, so an unknown name takes as long to refuse as a wrong password.
    private static readonly string UnknownUserHash = Hasher.HashPassword("", Guid.NewGuid().ToString());

    private readonly StagingDb _db;

    public UserStore(StagingDb db) => _db = db;

    private sealed class UserRow
    {
        public string UserName { get; init; } = "";
        public string PasswordHash { get; init; } = "";
        public bool IsDisabled { get; init; }
        public bool IsLocked { get; init; }
    }

    /// <summary>Trims the user name and checks both. Throws ArgumentException with a message a person can act on.</summary>
    public static string Clean(string? userName, string? password)
    {
        var name = (userName ?? "").Trim();
        if (name.Length is 0 or > 100)
            throw new ArgumentException("A user name is 1 to 100 characters.");
        if ((password ?? "").Length < MinPasswordLength)
            throw new ArgumentException($"A password is at least {MinPasswordLength} characters.");
        return name;
    }

    /// <summary>Adds the user, or gives an existing one a new password and clears its lockout. True when the user is new.</summary>
    public async Task<bool> SetPasswordAsync(string userName, string password, CancellationToken ct = default)
    {
        var name = Clean(userName, password);
        await using var cn = await _db.OpenStagingAsync(ct);
        var affected = await cn.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO Users (UserName, PasswordHash, CreatedAt) VALUES (@name, @hash, UTC_TIMESTAMP())
            ON DUPLICATE KEY UPDATE PasswordHash = @hash, FailedLogins = 0, LockedUntil = NULL
            """,
            new { name, hash = Hasher.HashPassword(name, password) }, cancellationToken: ct));
        return affected == 1;
    }

    /// <summary>Checks a sign-in and records its outcome. On Ok, the user's name as stored is returned beside it.</summary>
    public async Task<(SignIn Result, string? UserName)> SignInAsync(string? userName, string? password, CancellationToken ct = default)
    {
        var name = (userName ?? "").Trim();
        password ??= "";
        await using var cn = await _db.OpenStagingAsync(ct);
        var user = await cn.QuerySingleOrDefaultAsync<UserRow>(new CommandDefinition(
            """
            SELECT UserName, PasswordHash, IsDisabled, COALESCE(LockedUntil > UTC_TIMESTAMP(), 0) AS IsLocked
            FROM Users WHERE UserName = @name
            """,
            new { name }, cancellationToken: ct));

        if (user is null)
        {
            Hasher.VerifyHashedPassword("", UnknownUserHash, password);
            return (SignIn.Invalid, null);
        }
        if (user.IsDisabled)
            return (SignIn.Disabled, null);
        if (user.IsLocked)
            return (SignIn.Locked, null);

        var verified = Hasher.VerifyHashedPassword(user.UserName, user.PasswordHash, password);
        if (verified == PasswordVerificationResult.Failed)
        {
            await cn.ExecuteAsync(new CommandDefinition(
                """
                UPDATE Users SET
                    LockedUntil = IF(FailedLogins + 1 >= @MaxFailures, UTC_TIMESTAMP() + INTERVAL @LockoutMinutes MINUTE, LockedUntil),
                    FailedLogins = IF(FailedLogins + 1 >= @MaxFailures, 0, FailedLogins + 1)
                WHERE UserName = @UserName
                """,
                new { user.UserName, MaxFailures, LockoutMinutes }, cancellationToken: ct));
            return (SignIn.Invalid, null);
        }

        var rehash = verified == PasswordVerificationResult.SuccessRehashNeeded ? Hasher.HashPassword(user.UserName, password) : null;
        await cn.ExecuteAsync(new CommandDefinition(
            """
            UPDATE Users SET FailedLogins = 0, LockedUntil = NULL, LastLoginAt = UTC_TIMESTAMP(),
                PasswordHash = COALESCE(@rehash, PasswordHash)
            WHERE UserName = @UserName
            """,
            new { user.UserName, rehash }, cancellationToken: ct));
        return (SignIn.Ok, user.UserName);
    }
}
