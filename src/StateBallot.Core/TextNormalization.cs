namespace StateBallot.Core;

/// <summary>Small text-cleanup helpers shared by HTML scrapers.</summary>
public static class TextNormalization
{
    /// <summary>
    /// Replaces &amp;nbsp; with a regular space, then collapses all runs of
    /// whitespace to single spaces and trims. AngleSharp's TextContent preserves
    /// source whitespace/entities verbatim, so every HTML scraper needs this
    /// before matching or displaying extracted text.
    /// </summary>
    public static string CollapseWhitespace(string text) =>
        string.Join(' ', text.Replace('\u00a0', ' ').Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Trim();
}
