namespace StateBallot.Staging.Tests;

public class UserRulesTests
{
    [Fact]
    public void Clean_TrimsTheUserName() =>
        Assert.Equal("kim", UserStore.Clean(" kim ", "correct horse battery"));

    [Theory]
    [InlineData("", "correct horse battery", "user name")]
    [InlineData("kim", "too short", "at least 12")]
    [InlineData("kim", null, "at least 12")]
    public void Clean_RefusesABlankNameOrShortPassword(string name, string? password, string message) =>
        Assert.Contains(message, Assert.Throws<ArgumentException>(() => UserStore.Clean(name, password)).Message);
}
