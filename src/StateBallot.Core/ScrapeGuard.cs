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
}
