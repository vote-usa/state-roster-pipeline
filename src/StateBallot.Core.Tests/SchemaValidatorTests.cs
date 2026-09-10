using System.Text.Json.Nodes;
using StateBallot.Core.Publishing;

namespace StateBallot.Core.Tests;

public class SchemaValidatorTests
{
    private static readonly string FixtureRoot = Path.Combine(AppContext.BaseDirectory, "Fixtures");
    private static readonly SchemaValidator Validator = new();

    [Fact]
    public void KnowsEveryOutputFile()
    {
        string[] known = Validator.KnownFileNames.Where(n => n != "common.json").Order().ToArray();
        string[] expected = ["candidates.json", "county_ballots.json", "county_directory.json", "elections.json", "measures.json", "proposed_measures.json", "run.json"];

        Assert.Equal(expected, known);
    }

    [Theory]
    [InlineData("wa/elections.json")]
    [InlineData("wa/candidates.json")]
    [InlineData("wa/measures.json")]
    [InlineData("wa/county_directory.json")]
    [InlineData("wa/county_ballots.json")]
    [InlineData("ca/candidates.json")]
    [InlineData("ca/measures.json")]
    [InlineData("wv/candidates.json")]
    [InlineData("wv/elections.json")]
    public void RealOutputSamplesValidate(string relative)
    {
        var errors = Validator.ValidateFile(Path.Combine(FixtureRoot, relative));

        Assert.Empty(errors);
    }

    [Fact]
    public void ValidateDirectory_WalksRecursively_AndSkipsUnknownFiles()
    {
        var errors = Validator.ValidateDirectory(FixtureRoot);

        Assert.Empty(errors);
    }

    [Fact]
    public void MissingRequiredKey_Fails()
    {
        var rows = LoadFixture("wa/candidates.json");
        rows[0]!.AsObject().Remove("candidate_name");

        var errors = Validator.ValidateJson("candidates.json", rows.ToJsonString());

        var error = Assert.Single(errors);
        Assert.Equal("/0", error.Location);
        Assert.Contains("candidate_name", error.Message);
    }

    [Fact]
    public void UnknownKey_Fails()
    {
        var rows = LoadFixture("wa/elections.json");
        rows[0]!.AsObject()["extra"] = "nope";

        var errors = Validator.ValidateJson("elections.json", rows.ToJsonString());

        Assert.Contains(errors, e => e.Location == "/0" && e.Message.Contains("additionalProperties"));
    }

    [Fact]
    public void NullWhereNotAllowed_Fails()
    {
        var rows = LoadFixture("wa/candidates.json");
        rows[1]!.AsObject()["office"] = null;

        var errors = Validator.ValidateJson("candidates.json", rows.ToJsonString());

        Assert.Contains(errors, e => e.Location == "/1/office");
    }

    [Theory]
    [InlineData("ocd-division/country:us/state:wa/county:king")]
    [InlineData("ocd-division/country:us/state:ca/cd:14")]
    [InlineData("ocd-division/country:us/state:wa/county:grays_harbor")]
    public void OcdDivisionId_AcceptsRealShapes(string id)
    {
        var rows = LoadFixture("wa/candidates.json");
        rows[0]!.AsObject()["ocd_division_id"] = id;

        Assert.Empty(Validator.ValidateJson("candidates.json", rows.ToJsonString()));
    }

    [Theory]
    [InlineData("ocd-division/country:us/state:WA")]
    [InlineData("country:us/state:wa")]
    [InlineData("ocd-division/country:us/state:wa/")]
    public void OcdDivisionId_RejectsMalformed(string id)
    {
        var rows = LoadFixture("wa/candidates.json");
        rows[0]!.AsObject()["ocd_division_id"] = id;

        Assert.Contains(Validator.ValidateJson("candidates.json", rows.ToJsonString()),
            e => e.Location == "/0/ocd_division_id");
    }

    [Fact]
    public void BadDate_Fails()
    {
        var rows = LoadFixture("wv/elections.json");
        rows[0]!.AsObject()["election_date"] = "05/12/2026";

        Assert.Contains(Validator.ValidateJson("elections.json", rows.ToJsonString()),
            e => e.Location == "/0/election_date");
    }

    [Fact]
    public void NotJson_ReportsParseError()
    {
        var path = Path.Combine(Path.GetTempPath(), $"candidates-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, "{ not json");
        try
        {
            var candidatePath = Path.Combine(Path.GetDirectoryName(path)!, "candidates.json");
            File.Copy(path, candidatePath, overwrite: true);
            var errors = Validator.ValidateFile(candidatePath);
            var error = Assert.Single(errors);
            Assert.Contains("not valid JSON", error.Message);
            File.Delete(candidatePath);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static JsonArray LoadFixture(string relative) =>
        JsonNode.Parse(File.ReadAllText(Path.Combine(FixtureRoot, relative)))!.AsArray();
}
