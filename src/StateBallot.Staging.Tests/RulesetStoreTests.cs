using StateBallot.Staging;
using Testcontainers.MySql;
using Xunit;

namespace StateBallot.Staging.Tests;

/// <summary>
/// RulesetStore against a throwaway MySQL. Tagged out of the default run; execute with:
///   dotnet test --filter Category=Integration
/// </summary>
[Trait("Category", "Integration")]
public sealed class RulesetStoreTests : IAsyncLifetime
{
    private readonly MySqlContainer _mysql = new MySqlBuilder("mysql:8.4")
        .WithDatabase("roster_staging")
        .WithUsername("roster")
        .WithPassword("roster")
        .Build();

    private RulesetStore _store = null!;

    public async Task InitializeAsync()
    {
        await _mysql.StartAsync();
        var db = new StagingDb(_mysql.GetConnectionString(), _mysql.GetConnectionString());
        await new Migrator(db.StagingConnectionString).ApplyAsync();
        _store = new RulesetStore(db);
    }

    public Task DisposeAsync() => _mysql.DisposeAsync().AsTask();

    private static TransformStep Step(string kind, params (string, string)[] p) =>
        new() { Kind = kind, Params = p.ToDictionary(x => x.Item1, x => x.Item2) };

    private Task<Ruleset> NewAsync(string name, params TransformStep[] steps) =>
        _store.CreateAsync(new Ruleset { Name = name, Steps = steps.ToList() });

    [Fact]
    public async Task Create_RoundTripsStepsInOrder_WithParams()
    {
        var created = await NewAsync("Clean name",
            Step("trim"),
            Step("regex_replace", ("pattern", "\\s+"), ("replacement", " ")),
            Step("upper"));

        Assert.True(created.RulesetId > 0);
        var fetched = await _store.GetAsync(created.RulesetId);
        Assert.Equal(["trim", "regex_replace", "upper"], fetched!.Steps.Select(s => s.Kind));
        Assert.Equal(" ", fetched.Steps[1].Params["replacement"]);
        Assert.Equal("false", fetched.Steps[1].Params["ignoreCase"]);
    }

    [Fact]
    public async Task Create_DuplicateName_Throws()
    {
        await NewAsync("Same", Step("trim"));
        await Assert.ThrowsAsync<ArgumentException>(() => NewAsync("Same", Step("upper")));
    }

    [Fact]
    public async Task Update_ReplacesEverything()
    {
        var created = await NewAsync("Before", Step("trim"), Step("upper"));

        var updated = await _store.UpdateAsync(created.RulesetId,
            new Ruleset { Name = "After", Description = "lower only", Steps = [Step("lower")] });

        Assert.Equal("After", updated!.Name);
        Assert.Equal("lower only", updated.Description);
        Assert.Equal("lower", Assert.Single(updated.Steps).Kind);
    }

    [Fact]
    public async Task Update_Missing_ReturnsNull() =>
        Assert.Null(await _store.UpdateAsync(999, new Ruleset { Name = "x", Steps = [Step("trim")] }));

    [Fact]
    public async Task Update_ToAnotherRulesetsName_Throws()
    {
        await NewAsync("Taken", Step("trim"));
        var other = await NewAsync("Other", Step("trim"));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _store.UpdateAsync(other.RulesetId, new Ruleset { Name = "Taken", Steps = [Step("trim")] }));
        Assert.Equal("Other", (await _store.GetAsync(other.RulesetId))!.Name);
    }

    [Fact]
    public async Task List_IsByName_WithEachRulesetsOwnSteps()
    {
        await NewAsync("b", Step("upper"));
        await NewAsync("a", Step("trim"), Step("lower"));

        var all = await _store.ListAsync();

        Assert.Equal(["a", "b"], all.Select(r => r.Name));
        Assert.Equal(2, all[0].Steps.Count);
        Assert.Single(all[1].Steps);
    }

    private static TransformStep Link(Ruleset r) => Step("ruleset", ("rulesetId", r.RulesetId.ToString()));

    [Fact]
    public async Task Create_LinkToAnotherRuleset_IsKept_AndExpandsToItsCurrentSteps()
    {
        var inner = await NewAsync("Inner", Step("trim"));
        var outer = await NewAsync("Outer", Link(inner), Step("upper"));
        Assert.Equal(["ruleset", "upper"], (await _store.GetAsync(outer.RulesetId))!.Steps.Select(s => s.Kind));

        // A live link: editing the inner ruleset changes what the outer one runs.
        await _store.UpdateAsync(inner.RulesetId, new Ruleset { Name = "Inner", Steps = [Step("lower"), Step("trim")] });
        var expanded = RulesetSteps.Expand(outer.Steps, await _store.ByIdAsync(), outer.RulesetId);
        Assert.Equal(["lower", "trim", "upper"], expanded.Select(s => s.Kind));
    }

    [Fact]
    public async Task Create_LinkToAMissingRuleset_Throws() =>
        await Assert.ThrowsAsync<ArgumentException>(() => NewAsync("Dangling", Step("ruleset", ("rulesetId", "999"))));

    [Fact]
    public async Task Update_ThatWouldLoop_Throws_AndChangesNothing()
    {
        var a = await NewAsync("A", Step("trim"));
        var b = await NewAsync("B", Link(a));

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            _store.UpdateAsync(a.RulesetId, new Ruleset { Name = "A", Steps = [Link(b)] }));
        Assert.Contains("'A' → 'B' → 'A'", ex.Message);
        Assert.Equal("trim", Assert.Single((await _store.GetAsync(a.RulesetId))!.Steps).Kind);
    }

    [Fact]
    public async Task Delete_WhileLinked_Throws_NamingTheUser_ThenWorksOnceUnlinked()
    {
        var inner = await NewAsync("Inner", Step("trim"));
        var outer = await NewAsync("Outer", Link(inner));

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _store.DeleteAsync(inner.RulesetId));
        Assert.Contains("ruleset 'Outer'", ex.Message);
        Assert.NotNull(await _store.GetAsync(inner.RulesetId));

        Assert.True(await _store.DeleteAsync(outer.RulesetId));
        Assert.True(await _store.DeleteAsync(inner.RulesetId));
    }

    [Fact]
    public async Task SetActive_DeactivatesAndReactivates_AndActiveListLeavesItOut()
    {
        var created = await NewAsync("Toggle", Step("trim"));
        Assert.True(created.IsActive);

        var off = await _store.SetActiveAsync(created.RulesetId, false);
        Assert.False(off!.IsActive);
        Assert.Empty(await _store.ListAsync(activeOnly: true));
        Assert.Single(await _store.ListAsync());

        Assert.True((await _store.SetActiveAsync(created.RulesetId, true))!.IsActive);
        Assert.Null(await _store.SetActiveAsync(999, false));
    }

    [Fact]
    public async Task Deactivate_WhileAnActiveRulesetLinksToIt_Throws_ButNotOnceThatOneIsDeactivated()
    {
        var inner = await NewAsync("Inner", Step("trim"));
        var outer = await NewAsync("Outer", Link(inner));

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _store.SetActiveAsync(inner.RulesetId, false));
        Assert.Contains("active ruleset 'Outer'", ex.Message);
        Assert.True((await _store.GetAsync(inner.RulesetId))!.IsActive);

        await _store.SetActiveAsync(outer.RulesetId, false);
        Assert.False((await _store.SetActiveAsync(inner.RulesetId, false))!.IsActive);
    }

    [Fact]
    public async Task Reactivate_WhileItLinksToADeactivatedRuleset_Throws()
    {
        var inner = await NewAsync("Inner", Step("trim"));
        var outer = await NewAsync("Outer", Link(inner));
        await _store.SetActiveAsync(outer.RulesetId, false);
        await _store.SetActiveAsync(inner.RulesetId, false);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _store.SetActiveAsync(outer.RulesetId, true));
        Assert.Contains("deactivated ruleset 'Inner'", ex.Message);

        await _store.SetActiveAsync(inner.RulesetId, true);
        Assert.True((await _store.SetActiveAsync(outer.RulesetId, true))!.IsActive);
    }

    [Fact]
    public async Task Summaries_Search_IgnoresCase_MatchesWildcardsLiterally_AndCombinesWithActive()
    {
        await NewAsync("Tidy name", Step("trim"));
        await NewAsync("Shout name", Step("upper"));
        await NewAsync("100% clean", Step("trim"));
        var old = await NewAsync("tidy_old", Step("trim"));
        await _store.SetActiveAsync(old.RulesetId, false);

        Assert.Equal(["Tidy name", "tidy_old"], (await _store.ListSummariesAsync(search: "TIDY")).Select(r => r.Name));
        Assert.Equal(["100% clean"], (await _store.ListSummariesAsync(search: "%")).Select(r => r.Name));
        Assert.Equal(["tidy_old"], (await _store.ListSummariesAsync(search: "_")).Select(r => r.Name));
        Assert.Equal(["Tidy name"], (await _store.ListSummariesAsync(activeOnly: true, search: " tidy ")).Select(r => r.Name));
        Assert.Equal(4, (await _store.ListSummariesAsync(search: "  ")).Count);

        var full = await _store.ListAsync(search: "shout");
        Assert.Equal("upper", Assert.Single(Assert.Single(full).Steps).Kind);
    }

    [Fact]
    public async Task Update_KeepsTheFlag()
    {
        var created = await NewAsync("Off", Step("trim"));
        await _store.SetActiveAsync(created.RulesetId, false);

        var updated = await _store.UpdateAsync(created.RulesetId, new Ruleset { Name = "Off", Steps = [Step("upper")] });

        Assert.False(updated!.IsActive);
    }

    [Fact]
    public async Task NewLinkToADeactivatedRuleset_Throws_ButAnExistingLinkSurvivesEdits()
    {
        var inner = await NewAsync("Inner", Step("trim"));
        var outer = await NewAsync("Outer", Link(inner));
        await _store.SetActiveAsync(outer.RulesetId, false);
        await _store.SetActiveAsync(inner.RulesetId, false);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => NewAsync("Another", Link(inner)));
        Assert.Contains("deactivated", ex.Message);

        var edited = await _store.UpdateAsync(outer.RulesetId, new Ruleset { Name = "Outer", Steps = [Link(inner), Step("upper")] });
        Assert.Equal(["trim", "upper"], RulesetSteps.Expand(edited!.Steps, await _store.ByIdAsync()).Select(s => s.Kind));
    }

    [Fact]
    public async Task Delete_RemovesIt()
    {
        var created = await NewAsync("Gone", Step("trim"));
        Assert.True(await _store.DeleteAsync(created.RulesetId));
        Assert.Null(await _store.GetAsync(created.RulesetId));
        Assert.False(await _store.DeleteAsync(created.RulesetId));
    }
}
