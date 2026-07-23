# state-roster-pipeline

Reusable collector for U.S. state ballot rosters: upcoming elections, candidates,
ballot measures, county elections office directory, and per-county ballots.

Currently implemented:
1. Washington (WA)
1. Texas (TX)
1. West Virginia (WV)

The structure is designed so additional states plug in without touching the shared code.

## Structure

```
state-roster-pipeline/
  src/
    StateBallot.Core/             # state-agnostic: models, CollectResult, IStateCollector,
                                  # HttpFetcher, JSON/CSV writers, sources.json writer
    StateBallot.States.{State}/   # scrapers/clients, selectors, source config, collector
    StateBallot.Cli/              # argument parsing + state registry
  data/
    wa/, tx/, wv/                 # per-state inputs (e.g. county_fips.json) and outputs
```

## Usage

```bash
cd state-roster-pipeline/src
dotnet run --project StateBallot.Cli                       # WA, current year
dotnet run --project StateBallot.Cli -- --state TX --dry-run   # fetch + counts, no writes
dotnet run --project StateBallot.Cli -- --year 2028        # back-fill a specific year
dotnet run --project StateBallot.Cli -- --out /tmp/ballots # alternate output root
```

Requires .NET 8 SDK. Projects are also included in the root `state-roster-pipeline.sln` under the
`state-roster-pipeline` solution folder.

## Adding a state

1. Create `StateBallot.States.<Xx>` (classlib, net8.0) referencing `StateBallot.Core`.
2. Implement `IStateCollector`: fetch elections, candidates, measures, the county
   directory, and per-county ballots from that state's official sources, fill
   `CollectResult` (including `SourceGroups` provenance and `NextRun`), and follow the
   conventions below.
3. Add a `data/<xx>/county_fips.json` (county name => FIPS) and any other data files.
4. Register the collector in the `StateCollectors` dictionary in
   `StateBallot.Cli/Runner.cs`.

Conventions every state collector must follow:

- Target year is a parameter, never hardcoded; "upcoming" means election date >= today
  in the target year (a non-current `--year` collects the whole year, for back-fills).
- County lists come from data files or the state's site at runtime, not code.
- CSS selectors/regexes centralized in one `Selectors` class; URLs in one config class.
- Fail loudly (naming URL + selector) when a page yields zero rows; never write empty
  outputs silently. Data not yet published is recorded in `CollectResult.Gaps`.
- Outputs deterministic: stable sorts, stable key order, so re-runs are idempotent.
- Ballotpedia is never a data source (verification only).
- Unknown values are null - never invented.

## Washington sources (`StateBallot.States.Wa`)

| Data | Source | Format |
| --- | --- | --- |
| Elections + county codes | `voter.votewa.gov/CandidateList.aspx` (dropdowns) | HTML |
| Candidates + local measures | `voter.votewa.gov/elections/voterguide.ashx` (VoteWA voters' guide API) | JSON |
| Statewide measures | `sos.wa.gov/so/node/12667` (Proposed Ballot Measure Information) | HTML (links to PDFs) |
| County elections offices | `sos.wa.gov/elections/voters/voter-registration/county-elections-offices` | HTML |

## Texas sources (`StateBallot.States.Tx`)

| Data | Source | Format |
| --- | --- | --- |
| Elections | `goelect.txelections.civixapps.com` `getElectionsByYear` (CivixApps CBP API) | JSON |
| Candidates | `goelect.txelections.civixapps.com` `findQualifiedCandidates` (CivixApps CBP API) | JSON (POST) |

Texas notes: the API is Cloudflare-fronted and requires browser-like headers
(`TxSourceConfig.ExtraHeaders`, applied once per run - see `HttpFetcher.AddDefaultHeader`).
No county or district attribution is published; `county`/`district` are always null.
`party` is the raw single-letter source code (e.g. "R"/"D"), not expanded to a full name.

## West Virginia sources (`StateBallot.States.Wv`)

| Data | Source | Format |
| --- | --- | --- |
| Candidates (elections derived from these) | `candidates.wvsos.gov/candidate-web-api/candidates` | JSON (POST, paginated) |

West Virginia notes: there is no standalone election-catalog endpoint - each candidate
record carries its own election name/date/type, so `elections.*` is derived by grouping
candidates on their `electionId`. `county` reflects the candidate's own residential
county (a proxy for jurisdiction, not necessarily the race's actual jurisdiction).

## Outputs (`data/<state>/`)

`elections.json|csv`, `candidates.json|csv`, `measures.json|csv` (statewide proposed +
local measures), `county_directory.json`, `county_ballots.json|csv`, `sources.json`
(provenance with URL + format per data group, known gaps, and a machine-readable
`next_run` recommendation).

`candidates.*` carries a canonical set of fields across every state (see
`StateBallot.Core/Models.cs`'s `CandidateData`): beyond the original WA-derived fields,
it also includes `source_candidate_id`, `filing_date`, `email`, `phone`,
`campaign_phone`, `website`, `occupation`, and mailing/residential address fields -
populated where a state's source publishes them, null otherwise.

Notes on semantics:

- `county` is null for statewide/federal/legislative/judicial rows; local races that
  span counties list all counties joined with `"; "`.
- `incumbent` is null where the source does not publish incumbency (VoteWA does not).
- `party` reflects Washington's candidate-stated party preference (e.g. "Democratic
  Party", "GOP Party", "No Party Preference"); judicial and most local offices are
  nonpartisan (null).
- Statewide proposed measures have a null `election_date` until the Secretary of State
  certifies them to a ballot.
