# state-roster-pipeline

Reusable collector for U.S. state ballot rosters: upcoming elections, candidates,
ballot measures, county elections office directory, and per-county ballots.

Currently implemented:
1. Washington (WA)
1. California (CA)
1. Texas (TX)
1. West Virginia (WV)

The structure is designed so additional states plug in without touching the shared code.

Published roster snapshots: [vote-usa/state-roster-data](https://github.com/vote-usa/state-roster-data)
(pointer: [`data/input/snapshot.json`](data/input/snapshot.json)).

## Structure

```
state-roster-pipeline/
  src/
    StateBallot.Core/        # models, output DTOs, catalog, discovery, filters/sort,
                             # HttpFetcher, ResultWriter, OcdDivisionId
    StateBallot.States.{Xx}/ # scrapers, selectors, source config, collector, schedule
    StateBallot.Cli/         # args + catalog/discovery runner
  data/
    input/
      state_catalog.json     # all 50 states + DC (implemented | unimplemented)
      ca/ wa/ …              # county_fips.json + sources.json (tracked)
    output/
      ca/ wa/ …              # generated roster outputs (gitignored)
```

## Usage

```bash
cd state-roster-pipeline/src
dotnet run --project StateBallot.Cli                       # WA, current year
dotnet run --project StateBallot.Cli -- --state TX --dry-run   # fetch + counts, no writes
dotnet run --project StateBallot.Cli -- --year 2028        # back-fill a specific year
dotnet run --project StateBallot.Cli -- --out /tmp/ballots # alternate data root (input/ + output/)
```

Requires .NET 8 SDK. Roster outputs under `data/output/<xx>/` are gitignored —
re-run the collector to refresh them. Tracked inputs live under `data/input/`
(`state_catalog.json`, per-state `county_fips.json` and `sources.json`).

## Published data repo

Published snapshots go to
[vote-usa/state-roster-data](https://github.com/vote-usa/state-roster-data)
(`ca/`, `wa/`, … at the repo root). This repo keeps a pointer at
`data/input/snapshot.json`.

```bash
# Sync local data/output into ../state-roster-data, commit, update snapshot.json
# (does not push)
./scripts/sync-roster-data.sh

# Or collect straight into the data-repo checkout
dotnet run --project src/StateBallot.Cli -- \
  --state CA --input-root ./data --output-root ../state-roster-data
```

Details: [`logs/roster-data.md`](logs/roster-data.md).

## Adding a state

1. Flip the state to `implemented` in [`data/input/state_catalog.json`](data/input/state_catalog.json).
2. Create `StateBallot.States.<Xx>` with scrapers, `[StateCode("XX")]` collector,
   and `IPublishSchedule`; reference it from the Cli project (discovery finds it).
3. Add `data/input/<xx>/county_fips.json`.
4. Use Core helpers (`ElectionFilters`, `CollectResultSorter`, `SourcesManifest`,
   `RowHelpers`) — do not copy WA/CA private methods.

Conventions every state collector must follow:

- Target year is a parameter, never hardcoded; use `ElectionFilters` for "upcoming".
- County lists come from data files or the state's site at runtime, not code.
- CSS selectors/regexes centralized in one `Selectors` class; URLs in one config class.
- Fail loudly (naming URL + selector) when a page yields zero rows; never write empty
  outputs silently. Data not yet published is recorded in `CollectResult.Gaps`.
- Outputs deterministic via `CollectResultSorter.Sort`.
- Ballotpedia is never a data source (verification only).
- Unknown values are null - never invented.

## Washington sources (`StateBallot.States.Wa`)

| Data | Source | Format |
| --- | --- | --- |
| Elections + county codes | `voter.votewa.gov/CandidateList.aspx` (dropdowns) | HTML |
| Candidates + local measures | `voter.votewa.gov/elections/voterguide.ashx` (VoteWA voters' guide API) | JSON |
| Statewide measures | `sos.wa.gov/so/node/12667` (Proposed Ballot Measure Information) | HTML (links to PDFs) |
| County elections offices | `sos.wa.gov/elections/voters/voter-registration/county-elections-offices` | HTML |

## California sources (`StateBallot.States.Ca`)

| Data | Source | Format |
| --- | --- | --- |
| Statewide + special vacancy elections | `sos.ca.gov/elections/upcoming-elections` | HTML |
| Candidates | `elections.cdn.sos.ca.gov/statewide-elections/{year}-{primary\|general}/cert-list-candidates.pdf`; special elections link their certified list from their detail page | PDF |
| Statewide measures | `sos.ca.gov/elections/ballot-measures/qualified-ballot-measures` | HTML (links to full-text PDFs) |
| County-administered (local) elections | `sos.ca.gov/elections/upcoming-elections/county-administered-elections` | HTML |
| County elections offices | `sos.ca.gov/elections/voting-resources/county-elections-offices` | HTML |

California notes: statewide certified candidate lists post 68 days before election
day (Elections Code s. 8148); before that the election is recorded in `gaps` and
`next_run` points at the posting date. The SoS lists county-administered elections
but not their ballot content - those elections appear in `elections.*` with a gap
entry pointing at the county elections office site.

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

## Outputs (`data/output/<state>/`) and inputs (`data/input/`)

Roster outputs (gitignored): `elections.json|csv`, `candidates.json|csv`,
`measures.json|csv`, `county_directory.json`, `county_ballots.json|csv`.

Tracked inputs: `data/input/state_catalog.json`, `data/input/<state>/county_fips.json`,
and `data/input/<state>/sources.json` (provenance with URL + format per data group,
known gaps, and a machine-readable `next_run` recommendation).

`candidates.*` carries a canonical set of fields across every state (see
`StateBallot.Core/Models.cs`'s `CandidateRow`): beyond the original WA-derived fields,
it also includes `source_candidate_id`, `filing_date`, `email`, `phone`,
`campaign_phone`, `website`, `occupation`, and mailing/residential address fields -
populated where a state's source publishes them, null otherwise.

Notes on semantics:

- `county` is null for statewide/federal/legislative/judicial rows; local races that
  span counties list all counties joined with `"; "`.
- `ocd_division_id` is an [Open Civic Data](https://github.com/opencivicdata/ocd-division-ids)
  division identifier derived from state/office/district/county/jurisdiction (e.g.
  `ocd-division/country:us/state:ca/cd:14`). Null when the row's jurisdiction cannot
  be mapped confidently (underspecified local elections).
- `incumbent` is null where the source does not publish incumbency (VoteWA does not).
- `party` reflects Washington's candidate-stated party preference (e.g. "Democratic
  Party", "GOP Party", "No Party Preference"); judicial and most local offices are
  nonpartisan (null).
- Statewide proposed measures have a null `election_date` until the Secretary of State
  certifies them to a ballot.
