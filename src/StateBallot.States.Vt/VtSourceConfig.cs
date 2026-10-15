using StateBallot.Core;

namespace StateBallot.States.Vt;

/// <summary>
/// Named accessors over Vermont's source links (data/input/vt/source_links.json,
/// or the SourceLinks table when a run has a database). The URLs themselves are data.
/// </summary>
public sealed class VtSourceConfig : SourceConfigBase
{
    public string CandidatesPageUrl => Links.Url("candidates-page");

    /// <param name="kind">"Primary" or "General" (case-insensitive); anything else is treated as General.</param>
    public string CandidateListUrl(int year, string kind) => Links.Url("candidate-list", PrimaryOrGeneral(kind), year: year);
}
