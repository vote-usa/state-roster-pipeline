namespace StateBallot.Core;

/// <summary>
/// Generic first-occurrence deduplication. Each state collector supplies a key
/// selector over its own raw source type (composite keys work naturally since
/// tuples and anonymous types implement structural equality).
/// </summary>
public static class Deduplicator
{
    public static List<T> RemoveDuplicates<T>(IEnumerable<T> items, Func<T, object> keySelector)
    {
        var seen = new HashSet<object>();
        var result = new List<T>();
        foreach (var item in items)
        {
            if (seen.Add(keySelector(item)))
                result.Add(item);
        }
        return result;
    }
}
