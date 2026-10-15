using System.Reflection;
using StateBallot.Core.Raw;

namespace StateBallot.Core;

/// <summary>
/// Shared plumbing for a state collector. A state supplies its own capture
/// (<see cref="CaptureAsync"/>) and its parse-and-map step (<see cref="NormalizeCore"/>),
/// which also fills the source manifest. The base owns everything around them:
/// input-root resolution, the state code, lazily loaded input files, the
/// hollow-output guard, sorting and the next-run recommendation.
/// <para>
/// Discovery still needs a public constructor on the subclass, since constructors
/// aren't inherited. Declare it as a primary constructor that passes straight through:
/// <c>public sealed class XxCollector(int year, string stateDataDir, string? inputDataRoot = null, XxSourceConfig? config = null, SourceLinkSet? links = null)
/// : StateCollectorBase&lt;XxSourceConfig&gt;(year, stateDataDir, inputDataRoot, config, links)</c>.
/// </para>
/// <para>
/// URLs come from the state's <see cref="SourceLinkSet"/>: the one the runner passes in
/// (from the staging database, or a capture's snapshot), else data/input/&lt;xx&gt;/source_links.json.
/// </para>
/// </summary>
public abstract class StateCollectorBase<TConfig> : IStateCollector
    where TConfig : SourceConfigBase, new()
{
    private readonly Lazy<TConfig> _config;
    private readonly Lazy<string[]> _dateFormats;
    private readonly Lazy<Dictionary<string, string>> _fieldMap;

    /// <param name="stateDataDir">Per-state output directory (data/output/&lt;xx&gt;/). Only used to infer
    /// the input root when <paramref name="inputDataRoot"/> is not given.</param>
    /// <param name="config">A ready-made config, for tests. Otherwise one is built over <paramref name="links"/>.</param>
    /// <param name="links">The state's links; null loads the seed file under the input root.</param>
    protected StateCollectorBase(int year, string stateDataDir, string? inputDataRoot, TConfig? config, SourceLinkSet? links)
    {
        StateCode = GetType().GetCustomAttribute<StateCodeAttribute>()?.Code
            ?? throw new InvalidOperationException($"{GetType().FullName} is missing [StateCode(\"XX\")].");
        Year = year;
        InputDataRoot = ResolveInputDataRoot(stateDataDir, inputDataRoot);
        _config = new(() => config ?? new TConfig { Links = links ?? SourceLinkSet.Load(InputDataRoot, StateCode) });
        _dateFormats = new(() => DateFormatConfig.Load(DataPaths.DateFormatsPath(InputDataRoot, StateCode)));
        _fieldMap = new(() => LookupTableLoader.Load(DataPaths.CandidateFieldMapPath(InputDataRoot, StateCode)));
    }

    /// <summary>Two-letter state code, read from the class's [StateCode] attribute.</summary>
    public string StateCode { get; }

    /// <summary>Target election year. When normalizing, the capture's year.</summary>
    protected int Year { get; }

    /// <summary>Pipeline data root containing input/&lt;xx&gt;/.</summary>
    protected string InputDataRoot { get; }

    /// <summary>The state's named URLs, built on first use.</summary>
    protected TConfig Config => _config.Value;

    /// <summary>data/input/&lt;xx&gt;/date_formats.json, loaded on first use.</summary>
    protected string[] DateFormats => _dateFormats.Value;

    /// <summary>data/input/&lt;xx&gt;/candidate_field_map.json, loaded on first use.</summary>
    protected Dictionary<string, string> FieldMap => _fieldMap.Value;

    /// <summary>The source site to check by hand when a run comes back empty: the state's "home" link.</summary>
    protected virtual string SourceHomeUrl => Config.HomeUrl(Year);

    protected abstract IPublishSchedule Schedule { get; }

    public abstract Task CaptureAsync(HttpFetcher fetcher, DateOnly asOf);

    public CollectResult Normalize(CaptureReader capture)
    {
        var result = NormalizeCore(capture);
        EnsureNotHollow(result);
        CollectResultSorter.Sort(result);
        result.Sources.NextRun = Schedule.Recommend(result, Year);
        return result;
    }

    /// <summary>
    /// Builds the elections, rows and manifest source lists from the capture alone.
    /// The base sorts the result and sets the manifest's NextRun afterwards.
    /// </summary>
    protected abstract CollectResult NormalizeCore(CaptureReader capture);

    /// <summary>Refuses a result with no candidates. Override for a state that can legitimately return none.</summary>
    protected virtual void EnsureNotHollow(CollectResult result)
    {
        if (result.Candidates.Count == 0)
            throw new InvalidOperationException(
                $"No candidates collected; refusing to write hollow outputs. Check {SourceHomeUrl} manually.");
    }

    private static string ResolveInputDataRoot(string stateDataDir, string? inputDataRoot)
    {
        if (!string.IsNullOrWhiteSpace(inputDataRoot))
            return Path.GetFullPath(inputDataRoot);
        return DataPaths.TryInferPipelineDataRoot(stateDataDir)
            ?? throw new InvalidOperationException(
                $"Cannot infer input data root from output dir '{stateDataDir}'. " +
                "Pass inputDataRoot (CLI --input-root) when writing outside data/output/<xx>.");
    }
}
