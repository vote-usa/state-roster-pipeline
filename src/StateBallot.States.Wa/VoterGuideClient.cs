using System.Text.Json;
using System.Text.RegularExpressions;
using StateBallot.Core;
using StateBallot.Core.Raw;

namespace StateBallot.States.Wa;

// DTOs for the JSON returned by voter.votewa.gov/elections/voterguide.ashx.
public sealed class GuideResponse
{
    public string? Name { get; set; }
    public string? ElectionID { get; set; }
    public List<GuideCategory> Categories { get; set; } = new();
}

public sealed class GuideCategory
{
    public string? CategoryCode { get; set; }
    public string? Name { get; set; }
    public List<GuideRace> Races { get; set; } = new();
}

public sealed class GuideRace
{
    public string? RaceID { get; set; }
    public string? Name { get; set; }
    public string? MeasureName { get; set; }
    public string? ShortDescription { get; set; }
    public string? BallotTitle { get; set; }
    public string? CountyCode { get; set; }
    public string? Jurisdiction { get; set; }
    public bool MultiCounty { get; set; }
    public string? TextFor { get; set; }
    public string? TextAgainst { get; set; }
    public List<GuideCandidate> Candidates { get; set; } = new();
}

public sealed class GuideCandidate
{
    public string? BallotID { get; set; }
    public string? BallotName { get; set; }
    public string? PartyName { get; set; }
    public string? BallotDisplayOrder { get; set; }
}

public sealed class MeasureDetail
{
    public string? BallotTitle { get; set; }
    public string? AGShortDescription { get; set; }
    public string? ExplanatoryStatement { get; set; }
}

/// <summary>Client for the VoteWA online voters' guide JSON API.</summary>
public sealed class VoterGuideClient
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private static readonly Regex HtmlTags = new("<[^>]+>", RegexOptions.Compiled);

    public const string StatewideGuideRole = "statewide-guide";
    public const string CountyGuideRole = "county-guide";

    private readonly WaSourceConfig _config;

    public VoterGuideClient(WaSourceConfig config) => _config = config;

    /// <summary>Captures the guide for an election. countyCode "" is the statewide roll-up.</summary>
    public async Task<GuideResponse> CaptureGuideAsync(HttpFetcher fetcher, string electionId, string countyCode = "")
    {
        var url = _config.VoterGuideUrl(electionId, countyCode);
        var tag = countyCode.Length == 0
            ? FetchTag.Of(StatewideGuideRole, ("election", electionId))
            : FetchTag.Of(CountyGuideRole, ("election", electionId), ("county", countyCode));
        return ParseGuide(await fetcher.GetStringAsync(url, tag), url);
    }

    public static GuideResponse ParseGuide(string json, string url) =>
        JsonSerializer.Deserialize<GuideResponse>(json, JsonOptions)
        ?? throw new InvalidOperationException($"Empty voter guide response from {url}");

    /// <summary>"(Prefers Democratic Party)" => "Democratic Party"; nonpartisan/empty => null.</summary>
    public static string? NormalizeParty(string? partyName)
    {
        if (string.IsNullOrWhiteSpace(partyName))
            return null;
        var match = Selectors.PartyPreference.Match(partyName.Trim());
        if (!match.Success)
            return partyName.Trim();
        var party = match.Groups["party"].Value.Trim();
        return party.Length == 0 ? "No Party Preference" : party;
    }

    public static string? StripHtml(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return null;
        var text = HtmlTags.Replace(html, " ");
        text = System.Net.WebUtility.HtmlDecode(text);
        text = Regex.Replace(text, @"\s+", " ").Trim();
        return text.Length == 0 ? null : text;
    }
}
