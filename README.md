# state-roster-pipeline

Reusable collector for U.S. state ballot rosters: upcoming elections, candidates,
ballot measures, county elections office directory, and per-county ballots.

Currently implemented:
1. Washington (WA)
1. California (CA)
1. Texas (TX)
1. West Virginia (WV)
1. Maryland (MD)
1. North Carolina (NC)
1. Wyoming (WY)
1. Hawaii (HI)
1. Mississippi (MS)
1. Nebraska (NE)
1. Colorado (CO)
1. Vermont (VT)
1. Virginia (VA)
1. New Mexico (NM)

The structure is designed so additional states plug in without touching the shared code.
For the full architecture (config-driven surfaces, shared parsing/fetch primitives, the
mapper-class convention) and per-state implementation detail, see
[`configDrivenPipelineInfo.md`](configDrivenPipelineInfo.md). For what's next (remaining
states, fetch-strategy tiering, the field-mapping engine), see
[`configDrivenPipelinePlan.md`](configDrivenPipelinePlan.md).

## Structure

```
state-roster-pipeline/
  src/
    StateBallot.Core/        # models, output DTOs, catalog, discovery, filters/sort,
                             # HttpFetcher, ResultWriter, OcdDivisionId, generic
                             # CSV/XLSX parsers, WebFormsPostback, config loaders
    StateBallot.Core.Tests/  # tests for the shared Core primitives above
    StateBallot.States.{Xx}/ # scrapers, selectors, source config, mapper, collector,
                             # schedule - one project per implemented state
    StateBallot.States.{Xx}.Tests/  # mapper/parsing tests - one project per state
    StateBallot.Cli/         # args + catalog/discovery runner
  data/
    input/
      state_catalog.json     # all 50 states + DC (implemented | unimplemented)
      ca/ wa/ …               # county_fips.json, sources.json (tracked), plus
                              # per-state date_formats.json / selectors.json /
                              # election_type_names.json / election_ids.json
                              # where that state uses them
    output/
      ca/ wa/ …               # generated roster outputs (gitignored)
```

## Usage

```bash
cd state-roster-pipeline/src
dotnet run --project StateBallot.Cli                       # WA, current year
dotnet run --project StateBallot.Cli -- --state TX --dry-run   # fetch + counts, no writes
dotnet run --project StateBallot.Cli -- --year 2028        # back-fill a specific year
dotnet run --project StateBallot.Cli -- --out /tmp/ballots # alternate output root
```

Requires .NET 8 SDK. Roster outputs under `data/output/<xx>/` are gitignored —
re-run the collector to refresh them. Tracked inputs live under `data/input/`
(`state_catalog.json`, and per-state `county_fips.json`, `sources.json`, and
whichever of `date_formats.json` / `selectors.json` / `election_type_names.json` /
`election_ids.json` that state's collector loads - see
`configDrivenPipelineInfo.md` for which).

## Adding a state

1. Flip the state to `implemented` in [`data/input/state_catalog.json`](data/input/state_catalog.json).
2. Create `StateBallot.States.<Xx>` with a `SourceConfig` (URLs), a `Selectors`
   class (CSS selectors/regexes), a mapper (`ToElection`/`ToCandidateRow`, pure
   functions - see the mapper-class convention in `configDrivenPipelineInfo.md`),
   a `[StateCode("XX")]` collector, and an `IPublishSchedule`; reference it from
   the Cli project (`CollectorDiscovery` finds it via reflection - see
   `IStateCollector`'s doc comment for the required constructor shape).
3. Add `data/input/<xx>/date_formats.json` (required - `DateFormatConfig.Load`
   fails loudly if it's missing/empty) and `county_fips.json`.
4. If the source is a flat CSV/TSV or XLSX file, use `DelimitedTableParser` /
   `XlsxTableParser` (Core) rather than writing a new parser. If it's
   bot-blocked behind a WebForms postback (not a plain static file), see
   `WebFormsPostback` (Core).
5. Use Core helpers (`ElectionFilters`, `CollectResultSorter`, `SourcesManifest`,
   `RowHelpers`, `ScrapeGuard`, `TextNormalization`, `AddressFormatting`,
   `DateParsing`) - do not copy another state's private methods.
6. Write a `StateBallot.States.<Xx>.Tests` project covering the mapper logic,
   and live-verify the collector against the real source (dry-run + a real
   write, field-by-field) before considering the state done.

Conventions every state collector must follow:

- Target year is a parameter, never hardcoded; use `ElectionFilters` for "upcoming".
- County lists come from data files or the state's site at runtime, not code.
- CSS selectors/regexes centralized in one `Selectors` class; URLs in one config class.
- Fail loudly (naming URL + selector) when a page yields zero rows; never write empty
  outputs silently. Data not yet published is recorded in `CollectResult.Gaps`.
- Outputs deterministic via `CollectResultSorter.Sort`.
- Ballotpedia is never a data source (verification only).
- Unknown values are null - never invented.

## Sources by state

Full per-state detail (fetch mechanism, format quirks, what's out of scope) is in
[`configDrivenPipelineInfo.md`](configDrivenPipelineInfo.md#2-implemented-states).
Quick reference:

| State | Data | Source | Format |
| --- | --- | --- | --- |
| WA | Elections + county codes | `voter.votewa.gov/CandidateList.aspx` | HTML |
| WA | Candidates + local measures | `voter.votewa.gov/elections/voterguide.ashx` | JSON |
| WA | Statewide measures | `sos.wa.gov/so/node/12667` | HTML → PDF |
| WA | County elections offices | `sos.wa.gov/.../county-elections-offices` | HTML |
| CA | Statewide + special elections | `sos.ca.gov/elections/upcoming-elections` | HTML |
| CA | Candidates | `elections.cdn.sos.ca.gov/.../cert-list-candidates.pdf` | PDF |
| CA | Statewide measures | `sos.ca.gov/elections/ballot-measures/qualified-ballot-measures` | HTML → PDF |
| CA | County-administered elections + offices | `sos.ca.gov/elections/...` | HTML |
| TX | Elections | `goelect.txelections.civixapps.com` `getElectionsByYear` | JSON |
| TX | Candidates | `goelect.txelections.civixapps.com` `findQualifiedCandidates` | JSON (POST) |
| WV | Candidates (elections derived) | `candidates.wvsos.gov/candidate-web-api/candidates` | JSON (POST, paginated) |
| MD | Candidates (primary + general) | `elections.maryland.gov/elections/{year}/...` | CSV |
| MD | Election dates | `elections.maryland.gov/elections/{year}/index.html` | HTML |
| NC | Candidates + county ballots | `s3.amazonaws.com/dl.ncsbe.gov/Elections/{year}/...` | CSV |
| WY | Candidates (primary + general) | `.../2026_WY_{Primary,General}_Election_Candidates.csv` | CSV |
| WY | Election dates | Elections info page's embedded JSON-LD | HTML (JSON-LD) |
| HI | Candidates (single export) | `olvr.hawaii.gov/Controls/CandidateFiling.aspx`, "Export to CSV" | CSV (via WebForms POST) |
| HI | Election dates | `elections.hawaii.gov` home-page text widget | HTML |
| MS | Candidates (single export, elections derived) | `sos.ms.gov/content/CandidateQualifying/default.aspx`, "Download CSV" | CSV (via WebForms POST) |
| NE | Candidates + judicial retention (elections derived) | `sos.nebraska.gov/.../Statewide_Candidate_Filing_List.xlsx` | XLSX |
| NE | Election dates | `sos.nebraska.gov/elections` page text | HTML |
| CO | Candidates (primary + general) | `sos.state.co.us/.../{year}{Primary,General}CandidateList{Official,Unofficial}.xlsx` | XLSX |
| CO | Election dates | `sos.state.co.us/.../{year}ElectionCalendar.pdf` | HTML → PDF |
| VT | Candidates (primary + general) | `outside.vermont.gov/.../{year}_{statewide_primary,general_election}_qualified_candidates.xlsx` | XLSX |
| VT | Election dates | `sos.vermont.gov/elections/election-info-resources/candidates` page text | HTML |
| VA | Candidates (general only) | `elections.virginia.gov/casting-a-ballot/candidate-list/` → linked `.xlsx` | HTML → XLSX |
| VA | Election dates | Candidate list page's own `<title>` | HTML |
| NM | Candidates + judicial retention (general only) | `candidateportal.servis.sos.state.nm.us/CandidateList.aspx?eid=…` "Excel (xls)" export button | HTML table (mislabeled `.xls`) |
| NM | Election dates | `sos.nm.gov/voting-and-elections/view-all-elections/` page text | HTML |

Notable per-state quirks (see `configDrivenPipelineInfo.md` for the rest):

- **TX** is Cloudflare-fronted and needs browser-like headers
  (`TxSourceConfig.ExtraHeaders`); no county/district attribution; `party` is
  the raw single-letter code, not expanded.
- **WV** has no election-catalog endpoint - elections are derived by grouping
  candidates on their own `electionId`.
- **CA** reconciles two separate election sources and is the one state whose
  mapper logic is *not* centralized into one file (see the mapper-class
  convention exception, documented on `CaSelectors`).
- **HI** and **MS** are fetched via a simulated WebForms form-POST rather
  than a plain GET against a static file (see `WebFormsPostback`, Core).
- **MS** sits behind a bot rule that blocks realistic browser User-Agent
  strings (the opposite of TX's Cloudflare check, which blocks the *absence*
  of one) - `MsSourceConfig.ExtraHeaders` overrides the UA to a curl-like
  string instead.
- **NE** is the first state read from a second worksheet in the same
  workbook (`XlsxTableParser`'s `sheetIndex` parameter) - judicial retention
  questions live on sheet 1 and map to `StatewideProposedMeasures`, not
  `CandidateRow`.
- **CO**'s two candidate-list pages/XLSX files are never year-parameterized -
  they always reflect whatever the current cycle is - so the collector
  cross-checks each page's own "20NN Primary/General Election ... Candidate
  List" heading against the requested year before trusting its data;
  back-filling a past year isn't supported by this source. Both XLSX files
  also end with a literal "End of Data" sentinel footer row, filtered out by
  requiring a non-blank Office. v1 scope is candidates only (name/office/
  district/party) - no filing date, address, or contact info at all.
- **VT**'s two candidate XLSX files *are* year-templated and stable
  back to at least 2024, unlike CO's - but the election-dates page they're
  linked from is evergreen (current-cycle-only text), so only the current
  cycle's dates are available even when a past year's candidate roster is.
  VT's legislative districts are compound county-abbreviation + number codes
  ("ADD 1", "CHI CT 1", "BEN RUT") kept verbatim rather than digit-extracted -
  see `OcdDivisionId.HasDistrict` in Core, tightened here to require the
  *whole* value be numeric rather than merely contain a digit, since
  digit-extracting these would have silently merged every county's own
  district "1" onto the same wrong `sldu`/`sldl` id.
- **VA**'s candidate-list page/XLSX are discovered by scraping an evergreen
  index (no year-templatable URL exists anywhere in this source, and
  filenames carry ad hoc revision-date suffixes); v1 scope is the general
  election only - a primary, when VA runs one, is split into separate
  per-party/per-scope files with no consistent naming, unlike the single
  general "All Offices" list. The export repeats every federal/statewide race
  once per Virginia locality (~133 of them); `VaCandidateMapper` collapses
  that back to one row per candidacy, joining genuinely multi-locality local
  races (e.g. a town straddling two counties) with `"; "` per the county-list
  convention above. Also surfaced a real regression in `OcdDivisionId`: VA's
  federal "Member, House of Representatives" has no "US"/"United States"
  qualifier at all, so an earlier CO-motivated loosening of the state-house
  matcher was wrongly claiming it - `IsUsHouse`/`IsStateHouse` now key off an
  explicit `"state house of representatives"` qualifier instead.
- **NM**'s ASP.NET candidate portal exposes no discoverable index of its own
  election ids (no dropdown, no sibling links) and no live source for a
  *past* election's date once it's passed (the SOS site removes per-election
  date/results pages once they're no longer current) - so v1 scope is the
  general election only, with its id hand-maintained in
  `data/input/nm/election_ids.json` rather than derived from the year. The
  export's own CSV option has a real data-quality bug (unescaped commas in
  some fields shift every later column on that row, ~13% of rows) fixed by
  exporting "Excel (xls)" instead - actually a plain HTML `<table>` wearing a
  misleading extension (a common Telerik RadGrid trick), read via a new
  `HtmlTableParser` (Core) rather than a real spreadsheet library. Judicial
  retention rows map to `StatewideProposedMeasures` like NE's, not
  `CandidateRow` (no opponent, no party).

## Outputs (`data/output/<state>/`) and inputs (`data/input/`)

Roster outputs (gitignored): `elections.json|csv`, `candidates.json|csv`,
`measures.json|csv`, `county_directory.json`, `county_ballots.json|csv`.

Tracked inputs: `data/input/state_catalog.json`, `data/input/<state>/county_fips.json`,
and `data/input/<state>/sources.json` (provenance with URL + format per data group,
known gaps, and a machine-readable `next_run` recommendation) - plus, for states that
use them, `date_formats.json`, `selectors.json`, `election_type_names.json`, and
`election_ids.json` (see `configDrivenPipelineInfo.md` for what each does and its
fail-loud/permissive policy).

`candidates.*` carries a canonical set of fields across every state (see
`StateBallot.Core/Models.cs`'s `CandidateRow`): `source_candidate_id`, `filing_date`,
`email`, `phone`, `campaign_phone`, `website`, `occupation`, mailing/residential
address fields, and `status` (raw source text, e.g. `"Withdrawn - 02/19/2026"` -
not normalized into a closed set, since the vocabulary varies per state) -
populated where a state's source publishes them, null otherwise.

Notes on semantics:

- `county` is null for statewide/federal/legislative/judicial rows; local races that
  span counties list all counties joined with `"; "`.
- `ocd_division_id` is an [Open Civic Data](https://github.com/opencivicdata/ocd-division-ids)
  division identifier derived from state/office/district/county/jurisdiction (e.g.
  `ocd-division/country:us/state:ca/cd:14`). Null when the row's jurisdiction cannot
  be mapped confidently (underspecified local elections).
- `incumbent` is null where the source does not publish incumbency (VoteWA does not).
- `party` reflects each state's own source vocabulary as-published (e.g. WA's full
  "Democratic Party"/"No Party Preference" strings, TX's raw single-letter code);
  judicial and most local offices are nonpartisan (null).
- Statewide proposed measures have a null `election_date` until certified to a ballot.
