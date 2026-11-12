using System.Text.Json;
using Dapper;
using MySqlConnector;

namespace StateBallot.Staging;

/// <summary>
/// The URLs a person keeps for a state, or for counties and a city in it (RegionUrls), and every
/// version of them (RegionUrlVersions). Each change writes the URL as it now is and a version row in
/// one transaction. Nothing is removed: a delete marks the URL deleted and can be reverted.
/// Timestamps are read back as UTC ISO strings.
/// </summary>
public sealed class RegionUrlStore
{
    public const string Added = "added";
    public const string Edited = "edited";
    public const string Reverted = "reverted";
    public const string Deleted = "deleted";

    /// <summary>The URL types, as the console's picker lists them.</summary>
    public static readonly IReadOnlyList<string> UrlTypes = ["official_landing", "ballotpedia", "candidate_data", "measure_data"];

    private const string IsoFormat = "yyyy-MM-dd'T'HH:mm:ss'Z'";

    private readonly StagingDb _db;

    public RegionUrlStore(StagingDb db) => _db = db;

    public sealed record SaveUrl(string StateCode, IReadOnlyList<string> Counties, string? City, string Url, string UrlType, string EditedBy);

    public sealed class UrlView
    {
        public int UrlId { get; init; }
        public string StateCode { get; init; } = "";
        public IReadOnlyList<string> Counties { get; init; } = [];
        public string? City { get; init; }
        public string Url { get; init; } = "";
        public string UrlType { get; init; } = "";
        public string UpdatedAt { get; init; } = "";
        public string UpdatedBy { get; init; } = "";
        /// <summary>When a retrieve of this state last fetched this URL.</summary>
        public string? LastUsedAt { get; init; }
    }

    public sealed class VersionView
    {
        public int VersionId { get; init; }
        public int UrlId { get; init; }
        public string Action { get; init; } = "";
        public string StateCode { get; init; } = "";
        public IReadOnlyList<string> Counties { get; init; } = [];
        public string? City { get; init; }
        public string Url { get; init; } = "";
        public string UrlType { get; init; } = "";
        public string EditedAt { get; init; } = "";
        public string EditedBy { get; init; } = "";
        /// <summary>When a retrieve of the state last fetched the URL while this version was current.</summary>
        public string? LastUsedAt { get; init; }
    }

    /// <summary>A retrieve of a state that fetched a URL.</summary>
    public sealed record UrlUse(string StateCode, string Url, DateTime At);

    /// <summary>
    /// Trims and checks a URL before it is saved. Throws ArgumentException with a message a
    /// person can act on. Counties keep their order, without blanks or repeats.
    /// </summary>
    public static SaveUrl Clean(SaveUrl request)
    {
        var state = (request.StateCode ?? "").Trim().ToUpperInvariant();
        if (state.Length != 2)
            throw new ArgumentException("Choose a state.");

        var counties = (request.Counties ?? [])
            .Select(c => (c ?? "").Trim())
            .Where(c => c.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (counties.Any(c => c.Length > 100))
            throw new ArgumentException("A county name can be up to 100 characters.");

        var city = (request.City ?? "").Trim();
        if (city.Length > 100)
            throw new ArgumentException("A city name can be up to 100 characters.");

        var url = (request.Url ?? "").Trim();
        if (url.Length == 0)
            throw new ArgumentException("Enter a URL.");
        if (url.Length > 2048)
            throw new ArgumentException("A URL can be up to 2048 characters.");
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new ArgumentException("The URL must be a complete address starting with http:// or https://.");

        if (!UrlTypes.Contains(request.UrlType))
            throw new ArgumentException($"The URL type must be one of {string.Join(", ", UrlTypes)}.");

        var editedBy = (request.EditedBy ?? "").Trim();
        if (editedBy.Length is 0 or > 100)
            throw new ArgumentException("Enter your name (up to 100 characters). It is recorded on the edit.");

        return new SaveUrl(state, counties, city.Length > 0 ? city : null, url, request.UrlType, editedBy);
    }

    /// <summary>The latest use of a URL in a state at or after <paramref name="from"/> and before <paramref name="until"/>, when given.</summary>
    public static DateTime? LastUsed(IEnumerable<UrlUse> uses, string stateCode, string url, DateTime? from = null, DateTime? until = null)
    {
        var times = uses
            .Where(u => u.StateCode == stateCode && u.Url == url && (from is null || u.At >= from) && (until is null || u.At < until))
            .Select(u => (DateTime?)u.At);
        return times.Max();
    }

    /// <summary>The state's URLs that are not deleted, oldest first.</summary>
    public async Task<IReadOnlyList<UrlView>> ListAsync(string stateCode, CancellationToken ct = default)
    {
        await using var cn = await _db.OpenStagingAsync(ct);
        var rows = (await cn.QueryAsync<UrlRow>(new CommandDefinition(
            "SELECT * FROM RegionUrls WHERE StateCode = @state AND NOT IsDeleted ORDER BY UrlId",
            new { state = stateCode }, cancellationToken: ct))).AsList();
        var uses = await UsesAsync(cn, rows.Select(r => (r.StateCode, r.Url)), ct);
        return rows.Select(r => r.ToView(uses)).ToList();
    }

    /// <summary>The URL, or null when there is none. A deleted URL is returned too.</summary>
    public async Task<UrlView?> GetAsync(int urlId, CancellationToken ct = default)
    {
        await using var cn = await _db.OpenStagingAsync(ct);
        var row = await RowAsync(cn, null, urlId, ct);
        return row?.ToView(await UsesAsync(cn, [(row.StateCode, row.Url)], ct));
    }

    /// <summary>
    /// County names for the state's picker: every county a run of the state has listed, in its
    /// county directory or as a county ballot, and any already on one of its URLs.
    /// </summary>
    public async Task<IReadOnlyList<string>> CountiesAsync(string stateCode, CancellationToken ct = default)
    {
        await using var cn = await _db.OpenStagingAsync(ct);
        var collected = await cn.QueryAsync<string>(new CommandDefinition(
            """
            SELECT CountyName FROM PassCountyDirectory WHERE StateCode = @state
            UNION
            SELECT County FROM RunCountyBallots WHERE StateCode = @state
            """,
            new { state = stateCode }, cancellationToken: ct));
        var onUrls = await cn.QueryAsync<string>(new CommandDefinition(
            "SELECT CountiesJson FROM RegionUrls WHERE StateCode = @state AND NOT IsDeleted",
            new { state = stateCode }, cancellationToken: ct));

        return collected
            .Concat(onUrls.SelectMany(ParseCounties))
            .Select(c => c.Trim())
            .Where(c => c.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Adds a URL. The request must have been through <see cref="Clean"/>.</summary>
    public async Task<UrlView> AddAsync(SaveUrl url, CancellationToken ct = default)
    {
        await using var cn = await _db.OpenStagingAsync(ct);
        await using var tx = await cn.BeginTransactionAsync(ct);
        await RefuseDuplicateAsync(cn, tx, null, url, ct);
        var urlId = await cn.ExecuteScalarAsync<int>(new CommandDefinition(
            """
            INSERT INTO RegionUrls (StateCode, CountiesJson, City, Url, UrlType, IsDeleted, UpdatedAt, UpdatedBy)
            VALUES (@StateCode, @CountiesJson, @City, @Url, @UrlType, FALSE, UTC_TIMESTAMP(), @EditedBy);
            SELECT LAST_INSERT_ID();
            """,
            Args(url), tx, cancellationToken: ct));
        await WriteVersionAsync(cn, tx, urlId, Added, ct);
        await tx.CommitAsync(ct);
        return (await GetAsync(urlId, ct))!;
    }

    /// <summary>
    /// Saves a new version of a URL, or returns null when there is no such URL. The request must
    /// have been through <see cref="Clean"/>. A deleted URL can only be brought back by a revert.
    /// </summary>
    public async Task<UrlView?> UpdateAsync(int urlId, SaveUrl url, CancellationToken ct = default)
    {
        await using var cn = await _db.OpenStagingAsync(ct);
        await using var tx = await cn.BeginTransactionAsync(ct);
        var row = await RowAsync(cn, tx, urlId, ct);
        if (row is null)
            return null;
        if (row.IsDeleted)
            throw new ArgumentException($"URL {urlId} was deleted. Revert it from its history to use it again.");

        await RefuseDuplicateAsync(cn, tx, urlId, url, ct);
        await cn.ExecuteAsync(new CommandDefinition(
            """
            UPDATE RegionUrls SET StateCode = @StateCode, CountiesJson = @CountiesJson, City = @City, Url = @Url,
                UrlType = @UrlType, UpdatedAt = UTC_TIMESTAMP(), UpdatedBy = @EditedBy
            WHERE UrlId = @UrlId
            """,
            Args(url, urlId), tx, cancellationToken: ct));
        await WriteVersionAsync(cn, tx, urlId, Edited, ct);
        await tx.CommitAsync(ct);
        return await GetAsync(urlId, ct);
    }

    /// <summary>Marks a URL deleted. False when there is no such URL, or it was already deleted.</summary>
    public async Task<bool> DeleteAsync(int urlId, string editedBy, CancellationToken ct = default)
    {
        await using var cn = await _db.OpenStagingAsync(ct);
        await using var tx = await cn.BeginTransactionAsync(ct);
        var updated = await cn.ExecuteAsync(new CommandDefinition(
            "UPDATE RegionUrls SET IsDeleted = TRUE, UpdatedAt = UTC_TIMESTAMP(), UpdatedBy = @editedBy WHERE UrlId = @urlId AND NOT IsDeleted",
            new { urlId, editedBy }, tx, cancellationToken: ct));
        if (updated == 0)
            return false;
        await WriteVersionAsync(cn, tx, urlId, Deleted, ct);
        await tx.CommitAsync(ct);
        return true;
    }

    /// <summary>
    /// Makes an earlier version of a URL current again, as a new version, and undeletes the URL.
    /// Returns null when there is no such URL. Throws ArgumentException when the version is not one
    /// of the URL's, or is a delete.
    /// </summary>
    public async Task<UrlView?> RevertAsync(int urlId, int versionId, string editedBy, CancellationToken ct = default)
    {
        await using var cn = await _db.OpenStagingAsync(ct);
        await using var tx = await cn.BeginTransactionAsync(ct);
        if (await RowAsync(cn, tx, urlId, ct) is null)
            return null;
        var version = await cn.QueryFirstOrDefaultAsync<VersionRow>(new CommandDefinition(
            "SELECT * FROM RegionUrlVersions WHERE VersionId = @versionId AND UrlId = @urlId",
            new { versionId, urlId }, tx, cancellationToken: ct));
        if (version is null)
            throw new ArgumentException($"URL {urlId} has no version {versionId}.");
        if (version.Action == Deleted)
            throw new ArgumentException("That version is the delete. Revert to a version before it.");

        await cn.ExecuteAsync(new CommandDefinition(
            """
            UPDATE RegionUrls SET StateCode = @StateCode, CountiesJson = @CountiesJson, City = @City, Url = @Url,
                UrlType = @UrlType, IsDeleted = FALSE, UpdatedAt = UTC_TIMESTAMP(), UpdatedBy = @editedBy
            WHERE UrlId = @urlId
            """,
            new { version.StateCode, version.CountiesJson, version.City, version.Url, version.UrlType, editedBy, urlId },
            tx, cancellationToken: ct));
        await WriteVersionAsync(cn, tx, urlId, Reverted, ct);
        await tx.CommitAsync(ct);
        return await GetAsync(urlId, ct);
    }

    /// <summary>Every version of a URL, newest first.</summary>
    public Task<IReadOnlyList<VersionView>> HistoryAsync(int urlId, CancellationToken ct = default) =>
        VersionsWhereAsync("v.UrlId = @urlId", new { urlId }, ct);

    /// <summary>
    /// The state's change log: every version of every URL that has been in the state, newest first.
    /// A URL moved to another state keeps its whole history here, so each version can be compared with the one before.
    /// </summary>
    public Task<IReadOnlyList<VersionView>> StateHistoryAsync(string stateCode, CancellationToken ct = default) =>
        VersionsWhereAsync("v.UrlId IN (SELECT UrlId FROM RegionUrlVersions WHERE StateCode = @state)", new { state = stateCode }, ct);

    private async Task<IReadOnlyList<VersionView>> VersionsWhereAsync(string where, object args, CancellationToken ct)
    {
        await using var cn = await _db.OpenStagingAsync(ct);
        var rows = (await cn.QueryAsync<VersionRow>(new CommandDefinition(
            $"SELECT v.* FROM RegionUrlVersions v WHERE {where} ORDER BY v.VersionId DESC", args, cancellationToken: ct))).AsList();
        var uses = await UsesAsync(cn, rows.Select(r => (r.StateCode, r.Url)), ct);

        // A version was current from its edit until the next version of the same URL.
        var nextEdit = new Dictionary<int, DateTime>();
        var views = new List<VersionView>(rows.Count);
        foreach (var row in rows)
        {
            DateTime? until = nextEdit.TryGetValue(row.UrlId, out var next) ? next : null;
            views.Add(row.ToView(row.Action == Deleted ? null : LastUsed(uses, row.StateCode, row.Url, row.EditedAt, until)));
            nextEdit[row.UrlId] = row.EditedAt;
        }
        return views;
    }

    // Matched on the exact URL, since that is what a collector requests and records.
    private static async Task<IReadOnlyList<UrlUse>> UsesAsync(MySqlConnection cn, IEnumerable<(string State, string Url)> urls, CancellationToken ct)
    {
        var wanted = urls.Distinct().ToList();
        if (wanted.Count == 0)
            return [];
        return (await cn.QueryAsync<UrlUse>(new CommandDefinition(
            """
            SELECT DISTINCT c.StateCode, f.Url, c.StartedAt AS At
            FROM CaptureFetches f JOIN Captures c ON c.CaptureId = f.CaptureId
            WHERE c.StateCode IN @states AND f.Url IN @urls
            """,
            new { states = wanted.Select(w => w.State).Distinct(), urls = wanted.Select(w => w.Url).Distinct() },
            cancellationToken: ct))).AsList();
    }

    private static async Task RefuseDuplicateAsync(MySqlConnection cn, MySqlTransaction tx, int? urlId, SaveUrl url, CancellationToken ct)
    {
        var same = await cn.QueryAsync<UrlRow>(new CommandDefinition(
            """
            SELECT * FROM RegionUrls
            WHERE StateCode = @StateCode AND Url = @Url AND UrlType = @UrlType AND NOT IsDeleted AND UrlId <> @UrlId
            """,
            Args(url, urlId ?? 0), tx, cancellationToken: ct));
        var counties = url.Counties.Order(StringComparer.OrdinalIgnoreCase);
        var duplicate = same.FirstOrDefault(r =>
            string.Equals(r.City ?? "", url.City ?? "", StringComparison.OrdinalIgnoreCase) &&
            ParseCounties(r.CountiesJson).Order(StringComparer.OrdinalIgnoreCase).SequenceEqual(counties, StringComparer.OrdinalIgnoreCase));
        if (duplicate is not null)
            throw new ArgumentException($"That URL is already listed for the same region and type (URL {duplicate.UrlId}).");
    }

    private static Task<UrlRow?> RowAsync(MySqlConnection cn, MySqlTransaction? tx, int urlId, CancellationToken ct) =>
        cn.QueryFirstOrDefaultAsync<UrlRow>(new CommandDefinition(
            $"SELECT * FROM RegionUrls WHERE UrlId = @urlId{(tx is null ? "" : " FOR UPDATE")}", new { urlId }, tx, cancellationToken: ct));

    // Copies the URL as it now is, so the version row is exactly what was saved.
    private static Task WriteVersionAsync(MySqlConnection cn, MySqlTransaction tx, int urlId, string action, CancellationToken ct) =>
        cn.ExecuteAsync(new CommandDefinition(
            """
            INSERT INTO RegionUrlVersions (UrlId, Action, StateCode, CountiesJson, City, Url, UrlType, EditedAt, EditedBy)
            SELECT UrlId, @action, StateCode, CountiesJson, City, Url, UrlType, UpdatedAt, UpdatedBy FROM RegionUrls WHERE UrlId = @urlId
            """,
            new { urlId, action }, tx, cancellationToken: ct));

    private static object Args(SaveUrl url, int urlId = 0) => new
    {
        UrlId = urlId, url.StateCode, CountiesJson = JsonSerializer.Serialize(url.Counties), url.City, url.Url, url.UrlType, url.EditedBy,
    };

    private static IReadOnlyList<string> ParseCounties(string json) => JsonSerializer.Deserialize<List<string>>(json) ?? [];

    private static string? Iso(DateTime? at) => at?.ToString(IsoFormat);

    private sealed class UrlRow
    {
        public int UrlId { get; set; }
        public string StateCode { get; set; } = "";
        public string CountiesJson { get; set; } = "[]";
        public string? City { get; set; }
        public string Url { get; set; } = "";
        public string UrlType { get; set; } = "";
        public bool IsDeleted { get; set; }
        public DateTime UpdatedAt { get; set; }
        public string UpdatedBy { get; set; } = "";

        public UrlView ToView(IReadOnlyList<UrlUse> uses) => new()
        {
            UrlId = UrlId, StateCode = StateCode, Counties = ParseCounties(CountiesJson), City = City, Url = Url, UrlType = UrlType,
            UpdatedAt = Iso(UpdatedAt)!, UpdatedBy = UpdatedBy, LastUsedAt = Iso(LastUsed(uses, StateCode, Url)),
        };
    }

    private sealed class VersionRow
    {
        public int VersionId { get; set; }
        public int UrlId { get; set; }
        public string Action { get; set; } = "";
        public string StateCode { get; set; } = "";
        public string CountiesJson { get; set; } = "[]";
        public string? City { get; set; }
        public string Url { get; set; } = "";
        public string UrlType { get; set; } = "";
        public DateTime EditedAt { get; set; }
        public string EditedBy { get; set; } = "";

        public VersionView ToView(DateTime? lastUsed) => new()
        {
            VersionId = VersionId, UrlId = UrlId, Action = Action, StateCode = StateCode, Counties = ParseCounties(CountiesJson),
            City = City, Url = Url, UrlType = UrlType, EditedAt = Iso(EditedAt)!, EditedBy = EditedBy, LastUsedAt = Iso(lastUsed),
        };
    }
}
