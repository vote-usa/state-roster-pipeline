using StateBallot.Staging;
using Xunit;

namespace StateBallot.Staging.Tests;

public class RulesetStepsTests
{
    private static TransformStep Step(string kind, params (string, string)[] p) =>
        new() { Kind = kind, Params = p.ToDictionary(x => x.Item1, x => x.Item2) };

    private static TransformStep Link(int id) => Step("ruleset", ("rulesetId", id.ToString()));

    private static Dictionary<int, Ruleset> Saved(params (int Id, string Name, TransformStep[] Steps)[] rulesets) =>
        rulesets.ToDictionary(r => r.Id, r => new Ruleset { RulesetId = r.Id, Name = r.Name, Steps = r.Steps.ToList() });

    [Fact]
    public void Expand_ReplacesLinks_AtAnyDepth_InOrder()
    {
        var saved = Saved(
            (1, "Tidy", [Step("trim")]),
            (2, "Tidy and shout", [Link(1), Step("upper")]));

        var steps = RulesetSteps.Expand([Step("lower"), Link(2), Step("trim")], saved);

        Assert.Equal(["lower", "trim", "upper", "trim"], steps.Select(s => s.Kind));
    }

    [Fact]
    public void EvaluateSamples_EvaluatesEachSampleAlone_InOrder()
    {
        var results = RulesetSteps.EvaluateSamples(new MappingEvaluator(),
            [Step("trim"), Step("concat", ("separator", "+")), Step("upper")],
            [" a ", "b", null]);

        Assert.Equal(["A", "B", ""], results.Select(r => r.Value));
        Assert.All(results, r => Assert.Null(r.Error));
    }

    [Fact]
    public void EvaluateSamples_OneFailingSample_KeepsTheOthers()
    {
        var results = RulesetSteps.EvaluateSamples(new FailsOn("bad"), [Step("trim")], ["ok", "bad", "fine"]);

        Assert.Equal(["ok", null, "fine"], results.Select(r => r.Value));
        Assert.Equal([null, "boom on bad", null], results.Select(r => r.Error));
    }

    private sealed class FailsOn(string bad) : IMappingEvaluator
    {
        public string Evaluate(ResolvedMapping mapping, IReadOnlyDictionary<string, string> row)
        {
            var value = mapping.Inputs.Single().Input.ManualValue!;
            return value == bad ? throw new System.Text.RegularExpressions.RegexMatchTimeoutException($"boom on {bad}") : value;
        }
    }

    [Fact]
    public void RefuseNewInactiveLinks_OnlyRefusesInactiveOnesNotLinkedBefore()
    {
        var off = new Ruleset { RulesetId = 1, Name = "Off", IsActive = false };
        var on = new Ruleset { RulesetId = 2, Name = "On" };

        RulesetSteps.RefuseNewInactiveLinks([1, 2], alreadyLinked: [1], [off, on]);
        var ex = Assert.Throws<ArgumentException>(() => RulesetSteps.RefuseNewInactiveLinks([1, 2], [], [off, on]));
        Assert.Contains("'Off'", ex.Message);
    }

    [Fact]
    public void Expand_SameRulesetTwice_IsNotALoop()
    {
        var saved = Saved((1, "Tidy", [Step("trim")]));
        Assert.Equal(2, RulesetSteps.Expand([Link(1), Link(1)], saved).Count);
    }

    [Fact]
    public void Expand_MissingRuleset_Throws()
    {
        var ex = Assert.Throws<ArgumentException>(() => RulesetSteps.Expand([Link(9)], Saved()));
        Assert.Contains("No ruleset 9", ex.Message);
    }

    [Fact]
    public void Expand_LinkBackToTheOwner_IsALoop_NamingThePath()
    {
        var saved = Saved(
            (1, "A", [Link(2)]),
            (2, "B", [Step("trim")]));

        // Saving B as "link to A" would make A → B → A.
        var ex = Assert.Throws<ArgumentException>(() => RulesetSteps.Expand([Link(1)], saved, ownerId: 2));
        Assert.Contains("'B' → 'A' → 'B'", ex.Message);
    }

    [Fact]
    public void Expand_LinkToItself_IsALoop() =>
        Assert.Throws<ArgumentException>(() => RulesetSteps.Expand([Link(1)], Saved((1, "A", [Step("trim")])), ownerId: 1));

    [Fact]
    public void Resolve_RunsEachLevelsRulesetsInOrder()
    {
        var saved = Saved(
            (1, "Tidy", [Step("trim")]),
            (2, "Tidy and shout", [Link(1), Step("upper")]));
        var mapping = new TargetColumnMapping
        {
            TargetColumn = "candidateBallotName",
            Inputs = [new MappingInput { Kind = MappingInputKinds.Column, SourceColumn = "name", RulesetIds = [2] }],
            RulesetIds = [1, 2],
        };

        var resolved = RulesetSteps.Resolve(mapping, saved);

        Assert.Equal(["trim", "trim", "upper"], resolved.Steps.Select(s => s.Kind));
        Assert.Equal(["trim", "upper"], resolved.Inputs[0].Steps.Select(s => s.Kind));
        Assert.Equal("name", resolved.Inputs[0].Input.SourceColumn);
    }

    [Fact]
    public void Resolve_MissingRuleset_Throws() =>
        Assert.Throws<ArgumentException>(() =>
            RulesetSteps.Resolve(new TargetColumnMapping { Inputs = [], RulesetIds = [5] }, Saved()));

    [Fact]
    public void Resolve_EndToEnd_WithTheEvaluator()
    {
        var saved = Saved(
            (1, "Tidy", [Step("trim")]),
            (2, "Join", [Step("concat", ("separator", " ")), Step("upper")]));
        var mapping = new TargetColumnMapping
        {
            Inputs =
            [
                new MappingInput { Kind = MappingInputKinds.Column, SourceColumn = "first", RulesetIds = [1] },
                new MappingInput { Kind = MappingInputKinds.Column, SourceColumn = "last", RulesetIds = [1] },
            ],
            RulesetIds = [2],
        };
        var row = new Dictionary<string, string> { ["first"] = "  shelley ", ["last"] = " capito  " };

        Assert.Equal("SHELLEY CAPITO", new MappingEvaluator().Evaluate(RulesetSteps.Resolve(mapping, saved), row));
    }

    [Theory]
    [InlineData("7", true)]
    [InlineData(" 7 ", true)]
    [InlineData("0", false)]
    [InlineData("-1", false)]
    [InlineData("abc", false)]
    [InlineData("", false)]
    public void TryParseId_AcceptsPositiveIntegersOnly(string value, bool ok) =>
        Assert.Equal(ok, RulesetSteps.TryParseId(value, out _));
}
