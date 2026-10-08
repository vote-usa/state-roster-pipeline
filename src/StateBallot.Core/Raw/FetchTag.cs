namespace StateBallot.Core.Raw;

/// <summary>
/// Names a fetch for the normalize stage: the role says what the payload is to the
/// collector ("candidates-page", "county-guide") and the keys say which one it is
/// ({ "election": "123", "county": "01" }).
/// </summary>
public sealed class FetchTag
{
    private FetchTag(string role, IReadOnlyDictionary<string, string> keys)
    {
        Role = role;
        Keys = keys;
    }

    public string Role { get; }
    public IReadOnlyDictionary<string, string> Keys { get; }

    public static FetchTag Of(string role, params (string Key, string Value)[] keys) =>
        new(role, keys.ToDictionary(k => k.Key, k => k.Value, StringComparer.Ordinal));
}
