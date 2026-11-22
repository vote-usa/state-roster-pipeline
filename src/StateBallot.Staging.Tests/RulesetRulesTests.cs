namespace StateBallot.Staging.Tests;

public class RulesetRulesTests
{
    private static TransformStep Step(string kind, params (string, string)[] p) =>
        new() { Kind = kind, Params = p.ToDictionary(x => x.Item1, x => x.Item2) };

    private static Ruleset Named(string name, params TransformStep[] steps) =>
        new() { Name = name, Steps = steps.ToList() };

    [Fact]
    public void Clean_TrimsNameAndBlanksDescription_AndLowercasesKinds()
    {
        var r = RulesetStore.Clean(new Ruleset { Name = "  Tidy name ", Description = "  ", Steps = [Step(" TRIM ")] });

        Assert.Equal("Tidy name", r.Name);
        Assert.Null(r.Description);
        Assert.Equal("trim", Assert.Single(r.Steps).Kind);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Clean_BlankName_Throws(string name) =>
        Assert.Throws<ArgumentException>(() => RulesetStore.Clean(Named(name, Step("trim"))));

    [Fact]
    public void Clean_NoSteps_Throws() =>
        Assert.Throws<ArgumentException>(() => RulesetStore.Clean(Named("Empty")));

    [Fact]
    public void Clean_UnknownKind_NamesTheStep()
    {
        var ex = Assert.Throws<ArgumentException>(() => RulesetStore.Clean(Named("x", Step("trim"), Step("reverse"))));
        Assert.Contains("Transform 2", ex.Message);
    }

    [Fact]
    public void Clean_MissingRequiredParam_Throws() =>
        Assert.Throws<ArgumentException>(() => RulesetStore.Clean(Named("x", Step("regex_replace", ("pattern", "a")))));

    [Fact]
    public void Clean_InvalidPattern_Throws() =>
        Assert.Throws<ArgumentException>(() => RulesetStore.Clean(Named("x", Step("regex_extract", ("pattern", "(")))));

    [Fact]
    public void Clean_RulesetStep_StoresTheIdAsCanonicalDigits() =>
        Assert.Equal("7", RulesetStore.Clean(Named("x", Step("ruleset", ("rulesetId", " 007 ")))).Steps[0].Params["rulesetId"]);

    [Theory]
    [InlineData("abc")]
    [InlineData("0")]
    public void Clean_RulesetStep_WithoutAnId_Throws(string id) =>
        Assert.Throws<ArgumentException>(() => RulesetStore.Clean(Named("x", Step("ruleset", ("rulesetId", id)))));

    [Fact]
    public void Clean_RulesetStep_MissingTheId_Throws() =>
        Assert.Throws<ArgumentException>(() => RulesetStore.Clean(Named("x", Step("ruleset"))));

    [Fact]
    public void Clean_FillsDefaults_DropsUnknownParams_AndKeepsWhitespaceValues()
    {
        var r = RulesetStore.Clean(Named("x",
            Step("regex_replace", ("pattern", "\\s+"), ("replacement", " "), ("bogus", "1")),
            Step("concat", ("separator", ", "))));

        Assert.Equal(new Dictionary<string, string> { ["pattern"] = "\\s+", ["replacement"] = " ", ["ignoreCase"] = "false" }, r.Steps[0].Params);
        Assert.Equal(", ", r.Steps[1].Params["separator"]);
    }
}
