namespace StateBallot.Core;

/// <summary>Guard helpers for scrapers that must fail loudly on markup drift.</summary>
public static class ScrapeGuard
{
    /// <summary>
    /// Throws InvalidOperationException with the given (scraper-specific) message
    /// if the collection is empty. The message is only built on the failure path,
    /// so an interpolated diagnostic string costs nothing on the success path.
    /// </summary>
    public static void RequireAny<T>(IReadOnlyCollection<T> items, Func<string> message)
    {
        if (items.Count == 0)
            throw new InvalidOperationException(message());
    }

    /// <summary>
    /// Throws InvalidOperationException with the given (scraper-specific) message
    /// if the condition is false. For guards that can't be expressed as "a
    /// collection came back empty" - e.g. "no heading on the page matched at all",
    /// as distinct from "headings matched but none applied to this year".
    /// </summary>
    public static void Require(bool condition, Func<string> message)
    {
        if (!condition)
            throw new InvalidOperationException(message());
    }
}
