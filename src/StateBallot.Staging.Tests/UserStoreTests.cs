using Dapper;
using Testcontainers.MySql;

namespace StateBallot.Staging.Tests;

/// <summary>Console users and sign-in against a real MySQL. Needs Docker.</summary>
[Trait("Category", "Integration")]
public sealed class UserStoreTests : IAsyncLifetime
{
    private const string Password = "correct horse battery";

    private readonly MySqlContainer _mysql = new MySqlBuilder("mysql:8.4")
        .WithDatabase("roster_staging")
        .WithUsername("roster")
        .WithPassword("roster")
        .Build();

    private StagingDb _db = null!;
    private UserStore _users = null!;

    public async Task InitializeAsync()
    {
        await _mysql.StartAsync();
        _db = new StagingDb(_mysql.GetConnectionString(), _mysql.GetConnectionString());
        await new Migrator(_db.StagingConnectionString).ApplyAsync();
        _users = new UserStore(_db);
    }

    public Task DisposeAsync() => _mysql.DisposeAsync().AsTask();

    [Fact]
    public async Task SignIn_AcceptsTheRightPassword_AndNothingElse()
    {
        Assert.True(await _users.SetPasswordAsync(" kim ", Password));

        Assert.Equal((UserStore.SignIn.Ok, "kim"), await _users.SignInAsync("kim ", Password));
        Assert.Equal(UserStore.SignIn.Invalid, (await _users.SignInAsync("kim", Password + "!")).Result);
        Assert.Equal(UserStore.SignIn.Invalid, (await _users.SignInAsync("nobody", Password)).Result);

        await using var cn = await _db.OpenStagingAsync();
        var hash = await cn.ExecuteScalarAsync<string>("SELECT PasswordHash FROM Users WHERE UserName = 'kim'");
        Assert.DoesNotContain(Password, hash);
    }

    [Fact]
    public async Task SignIn_LocksOutAfterTooManyFailures_UntilANewPasswordIsSet()
    {
        await _users.SetPasswordAsync("kim", Password);
        for (var i = 0; i < UserStore.MaxFailures; i++)
            Assert.Equal(UserStore.SignIn.Invalid, (await _users.SignInAsync("kim", "wrong")).Result);

        Assert.Equal(UserStore.SignIn.Locked, (await _users.SignInAsync("kim", Password)).Result);

        Assert.False(await _users.SetPasswordAsync("kim", Password + " staple"));
        Assert.Equal(UserStore.SignIn.Invalid, (await _users.SignInAsync("kim", Password)).Result);
        Assert.Equal(UserStore.SignIn.Ok, (await _users.SignInAsync("kim", Password + " staple")).Result);
    }

    [Fact]
    public async Task SignIn_ClearsFailuresAfterASuccess()
    {
        await _users.SetPasswordAsync("kim", Password);
        for (var i = 0; i < UserStore.MaxFailures - 1; i++)
            await _users.SignInAsync("kim", "wrong");
        await _users.SignInAsync("kim", Password);

        Assert.Equal(UserStore.SignIn.Invalid, (await _users.SignInAsync("kim", "wrong")).Result);
        Assert.Equal(UserStore.SignIn.Ok, (await _users.SignInAsync("kim", Password)).Result);
    }

    [Fact]
    public async Task SignIn_RefusesADisabledUser()
    {
        await _users.SetPasswordAsync("kim", Password);
        await using (var cn = await _db.OpenStagingAsync())
            await cn.ExecuteAsync("UPDATE Users SET IsDisabled = 1 WHERE UserName = 'kim'");

        Assert.Equal(UserStore.SignIn.Disabled, (await _users.SignInAsync("kim", Password)).Result);
    }
}
