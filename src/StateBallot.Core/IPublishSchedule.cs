namespace StateBallot.Core;

/// <summary>
/// State-specific policy for when ballot data is typically published and when
/// to recommend the next collector run.
/// </summary>
public interface IPublishSchedule
{
    NextRunInfo Recommend(CollectResult result, int year);
}
