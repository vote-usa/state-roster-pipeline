namespace StateBallot.Staging.Tests;

public class BuildVersionTests
{
    private const string Sha = "e0893040aa762218b2950ea95ff2c4eddb946ebf";

    [Fact]
    public void Parse_SplitsVersionAndCommit() =>
        Assert.Equal(new BuildVersion("0.1.0", Sha), BuildVersion.Parse($"0.1.0+{Sha}"));

    [Fact]
    public void Parse_BuildOutsideGit_HasVersionOnly() =>
        Assert.Equal(new BuildVersion("0.1.0", null), BuildVersion.Parse("0.1.0"));

    [Theory]
    [InlineData("0.1.0+abc123")]
    [InlineData("0.1.0+not-a-sha-not-a-sha-not-a-sha-not-a-")]
    public void Parse_IgnoresAnythingButAFullSha(string informational) =>
        Assert.Equal(new BuildVersion("0.1.0", null), BuildVersion.Parse(informational));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Parse_Nothing_IsUnknown(string? informational) =>
        Assert.Equal(new BuildVersion(null, null), BuildVersion.Parse(informational));

    [Fact]
    public void Current_IsStampedFromDirectoryBuildProps()
    {
        // Tests build from the repo, so the SDK stamps both the version and the commit.
        Assert.Equal("0.1.0", BuildVersion.Current.Version);
        Assert.Matches("^[0-9a-f]{40}$", BuildVersion.Current.GitSha ?? "");
    }
}
