using StateBallot.Staging;
using Xunit;

namespace StateBallot.Staging.Tests;

public class MappingEvaluatorTests
{
    private static readonly Dictionary<string, string> Row = new()
    {
        ["candidateFirstName"] = "shelley",
        ["candidateLastName"] = "capito",
        ["candidateBallotName"] = "  Shelley Moore Capito  ",
        ["partyCode"] = "R",
        ["middle"] = "",
    };

    private static MappingInput Col(string c) => new() { Kind = MappingInputKinds.Column, SourceColumn = c };
    private static MappingInput Val(string v) => new() { Kind = MappingInputKinds.Manual, ManualValue = v };
    private static TransformStep Step(string kind, params (string, string)[] p) =>
        new() { Kind = kind, Params = p.ToDictionary(x => x.Item1, x => x.Item2) };

    private static readonly IMappingEvaluator Evaluator = new MappingEvaluator();

    private static string Eval(List<MappingInput> inputs, params TransformStep[] steps) =>
        Evaluator.Evaluate(new TargetColumnMapping { Inputs = inputs, Transform = steps.ToList() }, Row);

    [Fact]
    public void SingleColumn_NoSteps_PassesThrough() =>
        Assert.Equal("R", Eval([Col("partyCode")]));

    [Fact]
    public void ManualValue_IsUsedLiterally() =>
        Assert.Equal("WV", Eval([Val("WV")]));

    [Fact]
    public void MissingColumn_IsEmpty() =>
        Assert.Equal("", Eval([Col("notThere")]));

    [Fact]
    public void Concat_JoinsWithSeparator() =>
        Assert.Equal("shelley capito", Eval([Col("candidateFirstName"), Col("candidateLastName")], Step("concat", ("separator", " "))));

    [Fact]
    public void Concat_ThenUpper_Composes() =>
        Assert.Equal("SHELLEY CAPITO", Eval(
            [Col("candidateFirstName"), Col("candidateLastName")],
            Step("concat", ("separator", " ")), Step("upper")));

    [Fact]
    public void Coalesce_PicksFirstNonEmpty() =>
        Assert.Equal("capito", Eval([Col("middle"), Col("candidateLastName")], Step("coalesce")));

    [Fact]
    public void Trim_RemovesSurroundingWhitespace() =>
        Assert.Equal("Shelley Moore Capito", Eval([Col("candidateBallotName")], Step("trim")));

    [Fact]
    public void RegexReplace_Replaces() =>
        Assert.Equal("R.", Eval([Col("partyCode")], Step("regex_replace", ("pattern", "$"), ("replacement", "."))));

    [Fact]
    public void RegexExtract_DefaultGroupIsWholeMatch() =>
        Assert.Equal("capito", Eval([Col("candidateLastName")], Step("regex_extract", ("pattern", "[a-z]+"))));

    [Fact]
    public void RegexExtract_CaptureGroup()
    {
        var m = new TargetColumnMapping
        {
            Inputs = [Col("candidateBallotName")],
            Transform = [Step("trim"), Step("regex_extract", ("pattern", @"^(\S+)"), ("group", "1"))],
        };
        Assert.Equal("Shelley", Evaluator.Evaluate(m, Row));
    }

    [Fact]
    public void RegexReplace_IgnoreCase()
    {
        var value = Eval([Col("partyCode")], Step("regex_replace", ("pattern", "r"), ("replacement", "D"), ("ignoreCase", "true")));
        Assert.Equal("D", value);
    }

    [Fact]
    public void MultipleInputs_NoCombineStep_ConcatenatesImplicitly() =>
        Assert.Equal("shelleycapito", Eval([Col("candidateFirstName"), Col("candidateLastName")]));

    [Fact]
    public void PerInputTransform_PieceOfOneAllOfAnother()
    {
        // Initial of the first name + all of the last name, joined with a space.
        var first = new MappingInput
        {
            Kind = MappingInputKinds.Column, SourceColumn = "candidateFirstName",
            Transform = [Step("regex_extract", ("pattern", "^."))],
        };
        var last = Col("candidateLastName");
        var mapping = new TargetColumnMapping
        {
            Inputs = [first, last],
            Transform = [Step("concat", ("separator", " "))],
        };
        Assert.Equal("s capito", Evaluator.Evaluate(mapping, Row));
    }
}
