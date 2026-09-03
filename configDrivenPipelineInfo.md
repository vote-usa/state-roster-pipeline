# Config-Driven Pipeline: Architecture & State Implementations

This is the technical reference for how the collector pipeline is built, and what
each implemented state actually does. For the forward-looking plan (what's next,
triage of remaining states), see [`configDrivenPipelinePlan.md`](configDrivenPipelinePlan.md).

## 1. Overview

Every state plugs into the same shared pipeline through a small number of fixed
seams. Nothing in `StateBallot.Core` references any per-state type — all
per-state code lives in its own `StateBallot.States.<Xx>` project and is
discovered at runtime via reflection.

```
StateBallot.Core/            # shared models, config loaders, fetch/parse primitives,
                              # discovery, filtering/sorting, output writing
StateBallot.States.<Xx>/     # one project per state: SourceConfig, Selectors,
                              # mapper(s), scraper/client(s), PublishSchedule, Collector
StateBallot.States.<Xx>.Tests/  # mapper/parsing unit tests, one project per state
StateBallot.Cli/             # entrypoint: catalog + CollectorDiscovery + Runner
data/input/<xx>/             # tracked, hand-edited config: date_formats.json,
                              # selectors.json, county_fips.json, election_type_names.json
data/output/<xx>/            # generated rosters (gitignored)
```

### The collector contract

Every state implements `IStateCollector` (`StateCode`, `CollectAsync()`), marked
with `[StateCode("XX")]`, and exposes a public constructor callable as
`(HttpFetcher, int year, string stateDataDir)`. `CollectorDiscovery` finds every
such type across all loaded `StateBallot.States.*.dll` assemblies via reflection
— adding a state means writing a new project and referencing it from
`StateBallot.Cli`, never editing shared dispatch code. A duplicate `StateCode`
or a collector missing that exact constructor shape fails discovery loudly at
startup, not silently.

`CollectAsync()` returns a `CollectResult` (elections, candidates, measures,
county directory, county ballots, gaps, sources manifest). Every state also
supplies an `IPublishSchedule.Recommend(result, year)` — state-specific logic
for when ballot data is typically published, written into `sources.json`'s
`next_run` so a scheduler knows when re-running is worthwhile.

### The canonical model (`StateBallot.Core/Models.cs`)

`Election`, `CandidateRow`, `MeasureRow`, `CountyDirectoryRow`, `CountyBallot` are
shared, fixed shapes every state maps into. The governing rule is **never
invent a value; leave the field null** when a state's source doesn't publish
it (e.g. `Incumbent` is null for VoteWA, which doesn't publish incumbency).
`CandidateRow.Status` is the one field that intentionally stores a raw,
unnormalized string rather than a closed enum — the vocabulary (`"Withdrawn -
02/19/2026"`, `"Deceased - 10/23/2025"`, `"Elected After Primary"`, ...) varies
too much per state to normalize without losing information.

### Config-driven per-state surfaces

Four kinds of per-state values live in tracked JSON under `data/input/<xx>/`
rather than in C# — each with its own fail-loud-or-permissive loading policy,
chosen deliberately per what a wrong value would do downstream:

| File | Loader | Empty/missing behavior | Why |
| --- | --- | --- | --- |
| `date_formats.json` (bare array of .NET date format strings) | `DateFormatConfig.Load` | Throws | A parsed election date fans out to year filtering, sort order, publish-schedule math, and every shipped output row — a silent bad parse is worse than a loud failure at startup. |
| `election_type_names.json` (raw-code → canonical-name object, e.g. TX's `{"P": "Primary", ...}`) | `LookupTableLoader.Load` | Missing file throws; an empty/malformed object does not (per-key misses pass the raw code through unchanged) | A code not yet in the table is a legitimate "not modeled yet" case, not an error. |
| `selectors.json` (named CSS-selector/regex strings, CA and WA today) | Each state's own `Selectors.Load(path)` | Missing file or missing key throws | Selectors are load-bearing for every scrape; a missing pattern should fail at the exact call site that needed it. |
| `county_fips.json` (county name → FIPS code) | `CountyFipsLoader.LoadRequired` / `.LoadOrEmpty` | State's choice — CA/WA differ (WA treats a missing FIPS as an acceptable null enrichment field; CA treats the file as authoritative for the expected county set) | Two real states, two real different semantics — modeled as two explicit methods, not a boolean flag. |

`DataPaths` (Core) centralizes every one of these paths so no state hand-builds
`Path.Combine` logic itself: `DateFormatsPath`, `ElectionTypeNamesPath`,
`SelectorsPath`, `CountyFipsPath`, `SourcesPath`.

The `Selectors` pattern (see `CaSelectors`, WA's `Selectors`) is a `sealed
class` with `required ... { get; init; }` properties, a `Load(path)` factory
that deserializes a flat JSON object and compiles any regex-typed properties,
and a `Default` static property holding the exact same values compiled in —
`Default` is both what `selectors.json` is seeded from and what tests use to
avoid depending on file I/O. Not every state needs this — see below.

### The mapper-class convention

Per-state DTO-to-canonical-row logic is a pure, static, I/O-free function —
`Xx CandidateMapper.ToCandidateRow(row, election, sourceUrl)` — taking
primitives or a `Dictionary<string,string>` row and returning a `CandidateRow`.
This is deliberately separated from fetching/parsing so it can be unit tested
without touching the network. TX and WV each have a dedicated
`<Xx>CandidateMapper`; WA's is `WaMapper` (built from characterization tests
against the original inline code, per Phase 0 below); CA is the one documented
exception — its four row constructions are inseparable from their own parsers
(PDF line regex vs. DOM traversal vs. multi-paragraph gathering), so each stays
as an `internal static ToXyzRow(...)` method inside its own scraper/parser file
rather than one `CaMapper.cs`, with a `remarks` comment on `CaSelectors`
explaining why.

### Generic parsers (Core)

Two format-agnostic table readers exist for states whose source is a flat file
rather than HTML/JSON/PDF, both returning the same shape —
`List<Dictionary<string,string>>`, header-keyed, case-insensitive
(`StringComparer.OrdinalIgnoreCase`), and an **empty list (never null or an
exception) on empty/header-only input** — an empty source is a
`ScrapeGuard`-level concern, not a parsing one:

- `DelimitedTableParser.Parse(text, delimiter = ',')` — CsvHelper-backed;
  the same implementation serves both CSV and TSV via the delimiter parameter.
- `XlsxTableParser.Parse(xlsxBytes)` — ClosedXML-backed; reads the first
  worksheet's used range only.

Deliberately **no `IFormatParser` interface or format-string-keyed registry** —
every state dispatches directly to whichever library it needs
(AngleSharp/System.Text.Json/PdfPig/these two), since no state has ever needed
to pick a parser by a runtime `format` string. Revisit if that changes.

### Fetch primitives (`HttpFetcher`, Core)

A single `HttpClient` wrapper shared by a run, with a polite User-Agent,
250ms throttling between requests, and a 3-attempt retry with backoff on
transient errors:

- `GetStringAsync(url)` / `GetBytesAsync(url)` — plain GET (text/binary).
  `TryGetBytesAsync` returns null instead of throwing on a 4xx (a document not
  yet published, distinct from a real failure).
- `PostJsonAsync<T>(url, body)` — JSON POST (WV's paginated candidate API).
- `PostFormAsync(url, formFields)` — form-urlencoded POST, for replaying a
  WebForms postback (see below).
- `AddDefaultHeader(name, value)` — lets a collector add browser-like headers
  once at construction for a Cloudflare-fronted source (TX). Mutates the
  shared instance, so one `HttpFetcher` must not be reused across sources with
  conflicting header needs within a run.

`WebFormsPostback` (Core) is the newest fetch primitive: `HiddenFields(html)`
regex-extracts every hidden `<input>` name/value from raw HTML (deliberately
no DOM-parser dependency), and `ClickButtonAsync(fetcher, url, html,
buttonName)` replays those fields plus one button's name to simulate a form
submit. Built for Hawaii's Telerik "Export to CSV" button — a genuine
`<input type="submit">`, not a client-side-only `doPostBack` — so a plain
replay returns the full export directly with no headless browser needed. This
is the first concrete piece of what the plan calls "fetch-strategy tiering":
generalizing beyond plain-GET and TX's header-spoofed GET.

### Other shared utilities

- `TextNormalization.CollapseWhitespace` — whitespace/`&nbsp;` collapsing.
- `DateParsing.TryParseAny(value, formats, out result)` — tries each format in
  order; each call site still owns its own throw-vs-skip behavior on total
  failure.
- `AddressFormatting.FormatMailingLine(params string?[] parts)` — joins
  non-empty address parts; each state's thin wrapper still owns its own
  `address is null` short-circuit.
- `ScrapeGuard.RequireAny(items, () => "message")` — throws with a
  lazily-built diagnostic only on the empty-result path.
- `CollectResultSorter` — deterministic output ordering (`Election` sorted by
  its real `DateOnly`; row types sorted by their string `ElectionDate`, which
  works only because every mapper formats dates as `yyyy-MM-dd` on the way
  out).
- `OcdDivisionId` — derives an Open Civic Data division id from
  state/office/district/county/jurisdiction with zero per-state code.

### Testing & verification discipline

Every state has its own `.Tests` project covering mapper logic (pure
functions, no network). Beyond unit tests, every state onboarding in this repo
has been **live-verified against the real government source** before being
considered done: a dry run and a real write against current data, spot-checked
field-by-field, plus an explicit check that removing the state's
`date_formats.json` fails loudly rather than silently producing wrong output.
Ballotpedia is never a data source — verification only.

## 2. Implemented states

Nine states are implemented as of 2026-09-03: WA, CA, TX, WV (pre-dating this
consolidation effort) and MD, NC, WY, HI, MS (added on the CSV/XLSX parser
infrastructure, one per onboarding session). Each section below covers only
what's *unique* to that state — shared behavior is in section 1.

### Washington (`StateBallot.States.Wa`)

| Data | Source | Format |
| --- | --- | --- |
| Elections + county codes | `voter.votewa.gov/CandidateList.aspx` (dropdowns) | HTML |
| Candidates + local measures | `voter.votewa.gov/elections/voterguide.ashx` | JSON |
| Statewide measures | `sos.wa.gov/so/node/12667` | HTML → PDF links |
| County elections offices | `sos.wa.gov/.../county-elections-offices` | HTML |

Election discovery is a dropdown-fanout (enumerate every election in the
dropdown, then fetch each one's data) rather than a single catalog call. Party
is the candidate-stated preference string verbatim (e.g. "Democratic Party",
"No Party Preference"); most judicial/local offices are nonpartisan (null).
`Incumbent` is always null (VoteWA doesn't publish it). Mapping lives in
`WaMapper` (extracted from originally-inline collector code via
characterization tests, see Phase 0 history in the plan doc).
**Open item, not yet investigated**: VoteWA's own `CandidateList.aspx?e=...`
page is flagged in outside research as exposing a CSV export at the same URL
this scraper currently HTML-scrapes — unverified, flagged for whoever next
touches WA.

### California (`StateBallot.States.Ca`)

| Data | Source | Format |
| --- | --- | --- |
| Statewide + special vacancy elections | `sos.ca.gov/elections/upcoming-elections` | HTML |
| Candidates | `elections.cdn.sos.ca.gov/.../cert-list-candidates.pdf` (statewide); special elections link their own certified list | PDF |
| Statewide measures | `sos.ca.gov/elections/ballot-measures/qualified-ballot-measures` | HTML → full-text PDF |
| County-administered (local) elections | `sos.ca.gov/elections/upcoming-elections/county-administered-elections` | HTML |
| County elections offices | `sos.ca.gov/elections/voting-resources/county-elections-offices` | HTML |

The most structurally complex state: election discovery reconciles two
separate sources (the statewide page and the county-administered page) into
one election set, matching partly on `ElectionDate` equality. Statewide
certified candidate lists post 68 days before election day (Elec. Code §8148);
before that, the election is recorded with a `gaps` entry and `next_run`
points at the posting date. County-administered elections are listed but their
ballot content isn't — those elections appear in `elections.*` with a gap
pointing at the county site. This is the one state where mapper logic is
deliberately *not* centralized (see the mapper-class convention above) —
`internal static ToXyzRow` methods live inside each of its four
scraper/parser files instead.

### Texas (`StateBallot.States.Tx`)

| Data | Source | Format |
| --- | --- | --- |
| Elections | `goelect.txelections.civixapps.com` `getElectionsByYear` | JSON |
| Candidates | `goelect.txelections.civixapps.com` `findQualifiedCandidates` | JSON (POST) |

The API is Cloudflare-fronted and requires browser-like headers
(`TxSourceConfig.ExtraHeaders`, applied once via `HttpFetcher.AddDefaultHeader`)
— the original, and until Hawaii's WebForms work, only bot-blocking case in the
codebase. No county/district attribution is published (`county`/`district`
always null). `party` is the raw single-letter source code ("R"/"D"), not
expanded to a full name. `election_type_names.json` maps TX's `P`/`G`/`S`/`R`
codes to `Primary`/`General`/`Special`/`Runoff`.

### West Virginia (`StateBallot.States.Wv`)

| Data | Source | Format |
| --- | --- | --- |
| Candidates (elections derived from these) | `candidates.wvsos.gov/candidate-web-api/candidates` | JSON (POST, paginated) |

No standalone election-catalog endpoint exists — `elections.*` is derived by
grouping candidate records on their own `electionId`/name/date/type. `county`
reflects the candidate's own residential county (a proxy for jurisdiction, not
necessarily the race's actual jurisdiction).

### Maryland (`StateBallot.States.Md`)

| Data | Source | Format |
| --- | --- | --- |
| Candidates (primary + general, separate files) | `elections.maryland.gov/elections/{year}/{primary,general}_candidates/{year}_G{P,G}_...csv` | CSV |
| Election dates | `elections.maryland.gov/elections/{year}/index.html` (`<dt>Primary/General Election Day</dt><dd>date</dd>`) | HTML |

First state built on the generic CSV parser, and the first genuinely new state
added since the consolidation effort began. The two candidate CSVs are fully
public static files — no auth, no bot-blocking, despite that being the norm
for many other states' sources. The election-day page **comments out** a
past election's `<dt>/<dd>` block once it's over rather than deleting it —
confirmed live post-June-2026-primary, only the General block remained visible
— so `ElectionDayScraper`'s AngleSharp query naturally sees only
currently-relevant elections, no date-comparison filtering needed. `Status`
(the field this state's live data motivated adding to `CandidateRow`) carries
values well beyond Active/Withdrawn — `"Seeking the Nomination"`,
`"Deceased - <date>"`, `"Disqualified - <date>"`, `"Failed to Submit Required
Number of Signatures - <date>"`, `"Declined - <date>"` — confirming the
raw-passthrough design. **v1 scope: statewide candidates only** — MD's
separate `all_counties` CSV (local races) mixes county names and
within-county district labels in one column with no separate county field,
requiring order-dependent parsing similar to CA's cross-source reconciliation;
deliberately not attempted. No measures found for MD; no county directory.

### North Carolina (`StateBallot.States.Nc`)

| Data | Source | Format |
| --- | --- | --- |
| Candidates (elections + county ballots derived from these) | `s3.amazonaws.com/dl.ncsbe.gov/Elections/{year}/Candidate Filing/Candidate_Listing_{year}.csv` | CSV |

A single static S3 file, no auth/bot-blocking (despite the state being flagged
blocked in outside research). Unlike MD, this is already a full per-county
ballot listing: every row carries its own `election_dt` (discriminating
primary vs. general within one file — no separate date scrape needed at all)
and its own `county_name` (all 100 counties). The dedup rule that separates
statewide/multi-county races from genuinely local ones is purely data-driven:
group rows by `(contest_name, name_on_ballot)` within an election; a group
spanning more than one distinct county is multi-county (emit one deduped
county-less row into `Candidates`), a group under exactly one county is local
(left to the county-ballot path only). Every row also builds its own county's
`CountyBallot`, so full county-ballot coverage came "for free" from the same
pass — no separate per-county fetch needed, unlike every other state so far.
The only project with **no AngleSharp dependency at all** — the whole source
is one CSV. A real bug was found and fixed here via output inspection: the
office/district-splitting regex originally matched any trailing word after
"DISTRICT"/"SEAT", wrongly treating a job title containing the word "DISTRICT"
as a numbered district; fixed by requiring the trailing token start with a
digit, with a named regression test. Ballot measures (PDF-only) and a county
directory are not attempted.

### Wyoming (`StateBallot.States.Wy`)

| Data | Source | Format |
| --- | --- | --- |
| Candidates (primary + general, separate files) | `{...}/2026_WY_{Primary,General}_Election_Candidates.csv` | CSV |
| Election dates | Elections info page's embedded schema.org JSON-LD (`Event` nodes) | HTML (JSON-LD) |

Two static CSVs, no auth/bot-blocking despite being flagged blocked. The real
election dates live in a structured JSON-LD block nested arbitrarily deep
(`GovernmentOrganization → department[] → event[]`) inside the info page's
HTML — found by recursively walking the parsed `JsonElement` tree for any
object with `"@type": "Event"` rather than assuming a fixed path.
`Office Sought` needed a three-stage split (more involved than MD's or NC's
single regex): strip a redundant party suffix the Primary file appends but the
General file doesn't → try a judicial seat-code pattern (short code +
descriptive title) → try a bare trailing district number (no keyword, unlike
NC's "DISTRICT n"/"SEAT n") → else left unchanged — validated against every
distinct value across both live files (349 rows), not samples. WY's phone
column is explicitly `"Campaign Telephone"`, mapped to `CampaignPhone` (not
the generic `Phone`, which stays null for WY). `Status` is synthesized from a
separate `"Date Withdrawn"` column (`"Withdrawn - {date}"` when present) —
live data currently has zero withdrawals, confirmed genuine (WY tracks those
in a separate file this collector doesn't fetch). No county concept in this
export at all — flatter than MD or NC in that respect, no `CountyBallot`s. No
measures (full-text-PDF only, not attempted).

### Hawaii (`StateBallot.States.Hi`)

| Data | Source | Format |
| --- | --- | --- |
| Candidates (single export covers both elections) | `olvr.hawaii.gov/Controls/CandidateFiling.aspx?elid={id}`, "Export to CSV" button | CSV (via WebForms POST) |
| Election dates | `elections.hawaii.gov` home-page text widget | HTML (regex over plain text) |

The only state whose data-yielding action is a real form POST rather than a
plain GET against a static file — see `WebFormsPostback` in section 1. The
page itself is a paginated Telerik grid (~24 of 415 rows per view), never
scraped directly; the export button returns the complete dataset in one
response. HI's `elid` query parameter is opaque and not year-derivable (94 =
2026, 92 = 2024, 87 = 2022, no formula) — solved with a two-phase fetch: load
one known-good `elid` (`HiSourceConfig.BootstrapElectionId`) purely to read
its election dropdown, find the option labeled `"{year} Candidate Report"`,
then fetch that election's own page for its own hidden fields before clicking
export. A single export covers the whole cycle with no phase/date column of
its own beyond a free-text `Status` (7 values: Issued, Filed, In Primary, In
General, Withdrawn, Void, Elected After Primary) — attributing a row to
Primary vs. General needed an explicit rule (`HiCandidateMapper.
DetermineElection`): only `"In General"` means General; everything else,
including `"Elected After Primary"` (HI can decide some nonpartisan county
races outright in the primary), means Primary. Live-confirmed 278/137 split,
all "Elected After Primary" rows correctly landed in Primary. `Contests` uses
the same office/district-split pattern as NC/WY, plus a comma-separated
`CityStateZip` variant (`"HONOLULU, HI 96808"`) distinct from MD/WY's
space-separated one. No `CountyBallot`s (HI's county-level races are just
contests, no separate county attribution needed); no measures.

### Mississippi (`StateBallot.States.Ms`)

| Data | Source | Format |
| --- | --- | --- |
| Candidates (elections derived from these) | `sos.ms.gov/content/CandidateQualifying/default.aspx`, "Download CSV" button | CSV (via WebForms POST) |

Onboarded 2026-09-03, originally flagged Tier C ("verified blocked" — a bare
`curl` returns 403). Live investigation found the actual mechanism is the
*inverse* of TX's: MS sits behind an Akamai WAF rule that blocks realistic
*browser* User-Agent strings (Chrome/Firefox/Safari, and any unrecognized
custom string — including `HttpFetcher`'s own default
`"StateBallotRoster/1.0 (...)"`) while letting plain HTTP-tool User-Agents
(curl, python-requests, Wget) straight through untouched — confirmed via
direct requests with each. `MsSourceConfig.ExtraHeaders` overrides the UA to
`"curl/8.4.0"` rather than TX's browser-spoofing headers — same
`HttpFetcher.AddDefaultHeader` mechanism, opposite direction. The actual data
host (`sos.ms.gov`, reached via an iframe embedded in the public
`www.sos.ms.gov/elections-voting/candidate-qualifying-list` page) is a real
ASP.NET WebForms page with a "Download CSV" `<input type="submit">`, the
second confirmed use of `WebFormsPostback` (Core) after HI's — but simpler,
with no bootstrap/id-discovery phase: one static URL, GET then click.

The single export is a live "currently qualified" snapshot, not a fixed
as-of-filing-deadline list: partisan federal races (US Senate/US House) carry
both a `Primary Election` and a `General Election` date on every row
regardless of party, and nonpartisan judicial races (Court of Appeals,
Chancery Court, Circuit Court) plus active special elections instead carry a
single `Election` date. `MsCandidateMapper.DetermineElection` handles both
shapes: an `Election`-column row is matched by date against the discovered
elections; a Primary/General row is attributed by comparing *today's* date
against the row's own Primary date, not by trusting `Party` (independents and
minor-party rows carry the same populated Primary/General columns as
Democratic/Republican rows — MS only runs state-administered primaries for
those two parties, confirmed via the SOS's own qualifying-forms page listing
a separate "Independent Candidate Form", but that fact isn't reliably
encoded in the file itself at every point in the cycle). Live-verified
2026-09-03 (after the March 10 primary had already passed): all 190
candidates — including the Senate race's Republican incumbent, Democratic
nominee, and independent challenger — correctly landed in the General
election, none left in the (already-resolved) Primary. Judicial races split
a seat across two columns, `District` and `Place` (e.g. District 1 Place 1
vs. District 1 Place 2 are different races) — merged into the canonical
`District` field as `"{district} Place {place}"` since the model has no
separate Place slot. `FilingDate` carries the raw `"M/d/yyyy to M/d/yyyy"`
qualifying-period range verbatim (not a single date, unlike every other
state's FilingDate so far). No county concept in this export (judicial
district/place numbering is the closest analog); no measures attempted.
