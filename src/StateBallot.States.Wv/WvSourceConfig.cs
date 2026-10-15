using StateBallot.Core;

namespace StateBallot.States.Wv;

/// <summary>
/// Named accessors over West Virginia's source links (data/input/wv/source_links.json,
/// or the SourceLinks table when a run has a database). The URLs themselves are data.
/// </summary>
public sealed class WvSourceConfig : SourceConfigBase
{
    public string CandidatesUrl => Links.Url("candidates-page");
}
