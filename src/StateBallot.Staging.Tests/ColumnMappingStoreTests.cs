using StateBallot.Staging;
using Testcontainers.MySql;
using Xunit;

namespace StateBallot.Staging.Tests;

/// <summary>
/// Exercises ColumnMappingStore against a throwaway MySQL, like LiveCollectionIntegrationTests
/// but offline. Tagged out of the default run; execute with:
///   dotnet test --filter Category=Integration
/// </summary>
[Trait("Category", "Integration")]
public sealed class ColumnMappingStoreTests : IAsyncLifetime
{
    private readonly MySqlContainer _mysql = new MySqlBuilder("mysql:8.4")
        .WithDatabase("roster_staging")
        .WithUsername("roster")
        .WithPassword("roster")
        .Build();

    private ColumnMappingStore _store = null!;
    private RulesetStore _rulesets = null!;

    public async Task InitializeAsync()
    {
        await _mysql.StartAsync();
        var db = new StagingDb(_mysql.GetConnectionString(), _mysql.GetConnectionString());
        await new Migrator(db.StagingConnectionString).ApplyAsync();
        _store = new ColumnMappingStore(db);
        _rulesets = new RulesetStore(db);
    }

    public Task DisposeAsync() => _mysql.DisposeAsync().AsTask();

    private Task<MappingGroup> NewGroupAsync(string state = "WV", string name = "Test group") =>
        _store.CreateGroupAsync(new MappingGroup { Name = name, StateCode = state });

    private static MappingInput Col(string c) => new() { Kind = MappingInputKinds.Column, SourceColumn = c };
    private static MappingInput Val(string v) => new() { Kind = MappingInputKinds.Manual, ManualValue = v };
    private static TargetColumnMapping Map(string target, params MappingInput[] inputs) =>
        new() { TargetColumn = target, Inputs = inputs.ToList() };
    private static TargetColumnMapping Linked(string target, string column, params int[] rulesetIds) =>
        new() { TargetColumn = target, Inputs = [Col(column)], RulesetIds = rulesetIds.ToList() };
    private static TransformStep Step(string kind, params (string, string)[] p) =>
        new() { Kind = kind, Params = p.ToDictionary(x => x.Item1, x => x.Item2) };

    private async Task<int> RulesetAsync(string name, params TransformStep[] steps) =>
        (await _rulesets.CreateAsync(new Ruleset { Name = name, Steps = steps.ToList() })).RulesetId;

    [Fact]
    public async Task CreateGroup_AssignsIdAndTimestamps_AndNormalizesState()
    {
        var group = await _store.CreateGroupAsync(new MappingGroup
        {
            Name = "WV General 2026", StateCode = "wv", Year = 2026, ElectionType = "General",
        });

        Assert.True(group.MappingGroupId > 0);
        Assert.Equal("WV", group.StateCode);
        Assert.Equal(2026, group.Year);
        Assert.NotEqual(default, group.CreatedAt);

        var fetched = await _store.GetGroupAsync(group.MappingGroupId);
        Assert.Equal("WV General 2026", fetched!.Name);
    }

    [Fact]
    public async Task CreateGroup_BlankName_Throws() =>
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _store.CreateGroupAsync(new MappingGroup { Name = "  ", StateCode = "WV" }));

    [Fact]
    public async Task ListGroups_FiltersByState()
    {
        await NewGroupAsync("WV", "wv-a");
        await NewGroupAsync("TX", "tx-a");

        var wv = await _store.ListGroupsAsync("wv");

        Assert.All(wv, g => Assert.Equal("WV", g.StateCode));
        Assert.Contains(wv, g => g.Name == "wv-a");
        Assert.DoesNotContain(wv, g => g.Name == "tx-a");
    }

    [Fact]
    public async Task GetColumns_NewGroup_IsEmpty()
    {
        var group = await NewGroupAsync();
        Assert.Empty(await _store.GetColumnsAsync(group.MappingGroupId));
    }

    [Fact]
    public async Task SaveColumns_RoundTripsOrderedSources()
    {
        var group = await NewGroupAsync();
        await _store.SaveColumnsAsync(group.MappingGroupId,
        [
            Map("CandidateName", Col("candidateBallotName")),
            Map("MailingAddressLine", Col("mailingAddress.streetNumber"), Col("mailingAddress.street1"), Col("mailingAddress.street2")),
        ]);

        var loaded = await _store.GetColumnsAsync(group.MappingGroupId);

        var address = loaded.Single(c => c.TargetColumn == "MailingAddressLine");
        Assert.Equal(
            new[] { "mailingAddress.streetNumber", "mailingAddress.street1", "mailingAddress.street2" },
            address.Inputs.Select(i => i.SourceColumn));
        Assert.All(address.Inputs, i => Assert.Equal(MappingInputKinds.Column, i.Kind));
    }

    [Fact]
    public async Task SaveColumns_RoundTripsManualValues()
    {
        var group = await NewGroupAsync();
        await _store.SaveColumnsAsync(group.MappingGroupId,
        [
            Map("State", Val("WV")),
            Map("ElectionType", Val("General")),
        ]);

        var loaded = await _store.GetColumnsAsync(group.MappingGroupId);

        var state = loaded.Single(c => c.TargetColumn == "State").Inputs.Single();
        Assert.Equal(MappingInputKinds.Manual, state.Kind);
        Assert.Equal("WV", state.ManualValue);
        Assert.Null(state.SourceColumn);
    }

    [Fact]
    public async Task SaveColumns_PreservesMixedColumnAndManualOrder()
    {
        var group = await NewGroupAsync();
        await _store.SaveColumnsAsync(group.MappingGroupId,
        [
            Map("Office", Val("U.S. SENATE"), Col("officeName"), Val("(statewide)")),
        ]);

        var office = Assert.Single(await _store.GetColumnsAsync(group.MappingGroupId));
        Assert.Equal(
            new[] { ("manual", "U.S. SENATE"), ("column", "officeName"), ("manual", "(statewide)") },
            office.Inputs.Select(i => (i.Kind, (i.SourceColumn ?? i.ManualValue)!)));
    }

    [Fact]
    public async Task SaveColumns_AllowsOneSourceFeedingManyTargets()
    {
        var group = await NewGroupAsync();
        await _store.SaveColumnsAsync(group.MappingGroupId,
        [
            Map("County", Col("residentialAddress.countyDescription")),
            Map("ResidentialCounty", Col("residentialAddress.countyDescription")),
        ]);

        var loaded = await _store.GetColumnsAsync(group.MappingGroupId);

        Assert.Equal("residentialAddress.countyDescription", loaded.Single(c => c.TargetColumn == "County").Inputs.Single().SourceColumn);
        Assert.Equal("residentialAddress.countyDescription", loaded.Single(c => c.TargetColumn == "ResidentialCounty").Inputs.Single().SourceColumn);
    }

    [Fact]
    public async Task SaveColumns_ReplacesPreviousSelectionWholesale()
    {
        var group = await NewGroupAsync();
        await _store.SaveColumnsAsync(group.MappingGroupId, [Map("Office", Col("officeName"))]);
        await _store.SaveColumnsAsync(group.MappingGroupId, [Map("CandidateName", Col("candidateBallotName"))]);

        var single = Assert.Single(await _store.GetColumnsAsync(group.MappingGroupId));
        Assert.Equal("CandidateName", single.TargetColumn);
    }

    [Fact]
    public async Task SaveColumns_TrimsInputs_DropsBlanks_AndEmptyTargets_ButKeepsDuplicates()
    {
        var group = await NewGroupAsync();
        await _store.SaveColumnsAsync(group.MappingGroupId,
        [
            // duplicates are legitimate: each input can carry its own rulesets
            Map("Party", Col("  partyCode  "), Col("partyCode"), Col("  ")),
            Map("Email", Col(""), Val("   ")),
        ]);

        var loaded = await _store.GetColumnsAsync(group.MappingGroupId);

        Assert.Equal(new[] { "partyCode", "partyCode" }, loaded.Single(c => c.TargetColumn == "Party").Inputs.Select(i => i.SourceColumn));
        Assert.DoesNotContain(loaded, c => c.TargetColumn == "Email");
    }

    [Fact]
    public async Task SaveColumns_UnknownTargetColumn_Throws()
    {
        var group = await NewGroupAsync();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _store.SaveColumnsAsync(group.MappingGroupId, [Map("NotAColumn", Col("x"))]));
    }

    [Fact]
    public async Task SaveColumns_UnknownInputKind_Throws()
    {
        var group = await NewGroupAsync();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _store.SaveColumnsAsync(group.MappingGroupId,
                [Map("Office", new MappingInput { Kind = "bogus", SourceColumn = "x" })]));
    }

    [Fact]
    public async Task SaveColumns_UnknownGroup_Throws() =>
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _store.SaveColumnsAsync(999999, [Map("Office", Col("x"))]));

    [Fact]
    public async Task SaveColumns_RoundTripsRulesetsInOrder_AtTheTargetAndOnEachInput()
    {
        var join = await RulesetAsync("Join", Step("concat", ("separator", " ")));
        var shout = await RulesetAsync("Shout", Step("upper"));
        var group = await NewGroupAsync();
        await _store.SaveColumnsAsync(group.MappingGroupId,
        [
            new TargetColumnMapping
            {
                TargetColumn = "CandidateName",
                Inputs =
                [
                    new MappingInput { Kind = MappingInputKinds.Column, SourceColumn = "first", RulesetIds = [shout] },
                    new MappingInput { Kind = MappingInputKinds.Column, SourceColumn = "last" },
                ],
                RulesetIds = [join, shout, join],
            },
        ]);

        var loaded = Assert.Single(await _store.GetColumnsAsync(group.MappingGroupId));
        Assert.Equal([join, shout, join], loaded.RulesetIds);
        Assert.Equal([shout], loaded.Inputs[0].RulesetIds);
        Assert.Empty(loaded.Inputs[1].RulesetIds);
    }

    [Fact]
    public async Task SaveColumns_UnknownRuleset_Throws()
    {
        var group = await NewGroupAsync();
        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            _store.SaveColumnsAsync(group.MappingGroupId, [Linked("Office", "officeName", 999)]));
        Assert.Contains("No ruleset 999", ex.Message);
    }

    [Fact]
    public async Task SaveColumns_NonPositiveRulesetId_Throws()
    {
        var group = await NewGroupAsync();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _store.SaveColumnsAsync(group.MappingGroupId, [Linked("Office", "officeName", 0)]));
    }

    [Fact]
    public async Task DeleteRuleset_WhileAMappingReferencesIt_Throws_NamingTheGroup()
    {
        var tidy = await RulesetAsync("Tidy", Step("trim"));
        var group = await NewGroupAsync("WV", "WV general");
        await _store.SaveColumnsAsync(group.MappingGroupId,
            [new TargetColumnMapping { TargetColumn = "Office", Inputs = [new MappingInput { Kind = MappingInputKinds.Column, SourceColumn = "officeName", RulesetIds = [tidy] }] }]);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _rulesets.DeleteAsync(tidy));
        Assert.Contains("mapping group 'WV general'", ex.Message);

        await _store.SaveColumnsAsync(group.MappingGroupId, [Map("Office", Col("officeName"))]);
        Assert.True(await _rulesets.DeleteAsync(tidy));
    }

    [Fact]
    public async Task CloneGroup_CopiesMetadataAndColumnsIntoNewGroup()
    {
        var source = await _store.CreateGroupAsync(new MappingGroup
        {
            Name = "WV General 2026", StateCode = "WV", Year = 2026, ElectionType = "General",
        });
        await _store.SaveColumnsAsync(source.MappingGroupId,
        [
            Map("State", Val("WV")),
            Map("MailingAddressLine", Col("mailingAddress.street1"), Col("mailingAddress.street2")),
        ]);

        var clone = await _store.CloneGroupAsync(source.MappingGroupId, new MappingGroup
        {
            Name = "WV Primary 2026", StateCode = "WV", Year = 2026, ElectionType = "Primary",
        });

        Assert.NotEqual(source.MappingGroupId, clone.MappingGroupId);
        Assert.Equal("WV Primary 2026", clone.Name);
        Assert.Equal("Primary", clone.ElectionType);

        var cloned = await _store.GetColumnsAsync(clone.MappingGroupId);
        Assert.Equal("WV", cloned.Single(c => c.TargetColumn == "State").Inputs.Single().ManualValue);
        Assert.Equal(
            new[] { "mailingAddress.street1", "mailingAddress.street2" },
            cloned.Single(c => c.TargetColumn == "MailingAddressLine").Inputs.Select(i => i.SourceColumn));

        // the clone is independent: editing it leaves the source untouched
        await _store.SaveColumnsAsync(clone.MappingGroupId, [Map("State", Val("WV"))]);
        Assert.Equal(2, (await _store.GetColumnsAsync(source.MappingGroupId)).Count);
    }

    [Fact]
    public async Task CloneGroup_UnknownSource_Throws() =>
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _store.CloneGroupAsync(999999, new MappingGroup { Name = "x", StateCode = "WV" }));

    [Fact]
    public async Task Save_LinkToADeactivatedRuleset_OnlyWhenTheGroupAlreadyHadIt()
    {
        var tidy = await RulesetAsync("Tidy", Step("trim"));
        var kept = await NewGroupAsync("WV", "kept");
        var fresh = await NewGroupAsync("WV", "fresh");
        await _store.SaveColumnsAsync(kept.MappingGroupId, [Linked("CandidateName", "name", tidy)]);
        await _rulesets.SetActiveAsync(tidy, false);

        await _store.SaveColumnsAsync(kept.MappingGroupId,
            [Linked("CandidateName", "name", tidy), Linked("Office", "office", tidy)]);
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _store.SaveColumnsAsync(fresh.MappingGroupId, [Linked("CandidateName", "name", tidy)]));
    }
}
