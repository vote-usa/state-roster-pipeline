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

Six kinds of per-state values live in tracked JSON under `data/input/<xx>/`
rather than in C# — each with its own fail-loud-or-permissive loading policy,
chosen deliberately per what a wrong value would do downstream:

| File | Loader | Empty/missing behavior | Why |
| --- | --- | --- | --- |
| `date_formats.json` (bare array of .NET date format strings) | `DateFormatConfig.Load` | Throws | A parsed election date fans out to year filtering, sort order, publish-schedule math, and every shipped output row — a silent bad parse is worse than a loud failure at startup. |
| `election_type_names.json` (raw-code → canonical-name object, e.g. TX's `{"P": "Primary", ...}`) | `LookupTableLoader.Load` | Missing file throws; an empty/malformed object does not (per-key misses pass the raw code through unchanged) | A code not yet in the table is a legitimate "not modeled yet" case, not an error. |
| `candidate_field_map.json` (canonical `CandidateRow` field name → that state's own column name, e.g. `{"Party": "Party Affiliation", ...}`) | `LookupTableLoader.Load` (same loader, same permissive-empty semantics — it's the identical flat-string-map shape) | Missing file throws; a field simply absent from the map means "not published/needs a transform," not an error | See `CandidateFieldMapper` below — the Tier 2 config-table extraction from the collector-abstraction analysis. |
| `selectors.json` (named CSS-selector/regex strings, CA and WA today) | Each state's own `Selectors.Load(path)` | Missing file or missing key throws | Selectors are load-bearing for every scrape; a missing pattern should fail at the exact call site that needed it. |
| `county_fips.json` (county name → FIPS code) | `CountyFipsLoader.LoadRequired` / `.LoadOrEmpty` | State's choice — CA/WA differ (WA treats a missing FIPS as an acceptable null enrichment field; CA treats the file as authoritative for the expected county set) | Two real states, two real different semantics — modeled as two explicit methods, not a boolean flag. |
| `election_ids.json` (canonical election type name → that source's own opaque id, e.g. NM's `{"General": "2917"}` or SD's `{"Primary": "773", "General": "774"}`) | `LookupTableLoader.Load` (same loader/shape as the field map above) | Missing file throws; missing entry for the requested type throws with a message pointing at how to re-derive it | For a source with no discoverable index of its own current elections at all (no dropdown, no sibling links) — the id has to be found by a human once per cycle; see NmSourceConfig/SdSourceConfig. |

`DataPaths` (Core) centralizes every one of these paths so no state hand-builds
`Path.Combine` logic itself: `DateFormatsPath`, `ElectionTypeNamesPath`,
`CandidateFieldMapPath`, `SelectorsPath`, `CountyFipsPath`, `SourcesPath`,
`ElectionIdsPath`.

### `CandidateFieldMapper` (Core) — the config-table field mapping

`CandidateFieldMapper.Get(fieldMap, row, canonicalField)` applies one state's
`candidate_field_map.json` to one parsed row: looks up the canonical field
(passed via `nameof(CandidateRow.Party)` etc., so a rename is caught at
compile time) to find which raw column holds it, then returns that column's
trimmed value from the row (or null if either the state doesn't map the
field at all, or the row's own value is blank). Built for the CSV/XLSX-backed
states (MD, NC, WY, HI, MS, NE) once six data points confirmed which
`CandidateRow` fields are *always* a single-column passthrough with zero
transform logic across every state that publishes them: `Party`, `Email`,
`Phone`, `CampaignPhone`, `Website`, `Occupation`, `MailingAddressLine`,
`MailingCity`, `MailingState`, `MailingZip`, `FilingDate`,
`SourceCandidateId`, `Status`, `ResidentialCity` — each mapper calls
`CandidateFieldMapper.Get` only for the subset its own state's source
actually publishes as a plain column (e.g. WY's is the only state with
`CampaignPhone`; NC has `MailingCity`/`State`/`Zip` as three separate plain
columns where MD/WY/HI/NE need a regex split instead, so only NC's config
uses them). `Office`, `District`, and `CandidateName` are deliberately never
table-driven, even where a given state's source happens not to need a
transform for one of them — every state onboarded so far needs at least
conditional logic for Office/District (a split regex, even where it's a
no-op on non-matching input), and CandidateName is a composite (first +
last name columns) for MD — both stay hand-written per state. This mirrors
the collector-abstraction analysis's three-tier split: Tier 2 (a pure config
table, done here), Tier 3 (a shared regex-consuming primitive — the
office/district split and city/state/zip split remain candidates, not yet
extracted), and Tier 4 (must stay hand-written: election discovery, row→
election attribution, NC's county dedup, MS's district+place merge). Verified
as a pure refactor, not a behavior change: live dry-run candidate/measure
counts matched pre-refactor values for all six states, with field-level
values (Party/Email/Phone/ResidentialCity/FilingDate) spot-checked directly
against a real row for MS and NE.

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
- `XlsxTableParser.Parse(xlsxBytes, sheetIndex = 0)` — ClosedXML-backed; reads
  one worksheet's used range (the first, by default). The `sheetIndex`
  parameter was added for Nebraska, the first consumer to need a second sheet
  from the same workbook (its judicial-retention questions live on sheet 1
  alongside sheet 0's candidates) — every prior/default call site is
  unaffected.
- `HtmlTableParser.Parse(html, tableIndex = 0)` — AngleSharp-backed (added to
  Core's own dependencies for this, following the same one-library-per-parser
  pattern as the two above); reads a `<table>`'s rows into the same shape.
  Added for New Mexico's candidate export, which is nominally "Excel (xls)"
  but is actually a plain HTML table wearing a misleading extension/
  content-type (a common Telerik RadGrid trick) — reading it as HTML rather
  than trusting the *real* CSV export the same page offers sidesteps a
  genuine data-quality bug in that CSV (unescaped commas in some fields shift
  every later column on that row). A cell's text is read with each `<br>`
  treated as a space rather than silently vanishing (plain `TextContent`
  concatenates a multi-line cell's text nodes with no separator at all for a
  `<br>`, since it carries no text of its own) — needed for NM's own
  "Judicial Retention&lt;br /&gt;Judge of the Metropolitan Court DIVISION 2"
  contest names.

Deliberately **no `IFormatParser` interface or format-string-keyed registry** —
every state dispatches directly to whichever library it needs
(System.Text.Json/PdfPig/these three), since no state has ever needed
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
buttonName, extraFields = null)` replays those fields plus one button's name
to simulate a form submit. Built for Hawaii's Telerik "Export to CSV" button —
a genuine `<input type="submit">`, not a client-side-only `doPostBack` — so a
plain replay returns the full export directly with no headless browser
needed. The optional `extraFields` parameter was added for New Mexico, the
first consumer that also needs a *visible* `<select>` set to a specific value
(its export-format dropdown) before the button click is replayed — every
prior call site (HI, MS) omits it and is unaffected.
`TriggerPostbackAsync(fetcher, url, html, eventTarget, eventArgument = "")`
is its sibling, added for South Dakota: its own Telerik "Export to CSV"
button turned out to be `type="button"` with an inline
`onclick="__doPostBack(...)"` handler rather than HI's simpler genuine
`type="submit"`, so no literal `buttonName=value` field exists to add at
all — this instead sets `__EVENTTARGET`/`__EVENTARGUMENT` directly, replaying
exactly what that inline handler would have done immediately before calling
`form.submit()`. This whole class of helper is the first concrete piece of
what the plan calls "fetch-strategy tiering": generalizing beyond plain-GET
and TX's header-spoofed GET.

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

Seventeen states are implemented as of 2026-09-13: WA, CA, TX, WV (pre-dating
this consolidation effort) and MD, NC, WY, HI, MS, NE, CO, VT, VA, NM, SC,
SD, MT (added on the CSV/XLSX parser infrastructure, one per onboarding
session). Each section below covers only what's *unique* to that state —
shared behavior is in section 1.

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

### Nebraska (`StateBallot.States.Ne`)

| Data | Source | Format |
| --- | --- | --- |
| Candidates + judicial retention (elections derived from these) | `sos.nebraska.gov/.../Statewide_Candidate_Filing_List.xlsx` | XLSX |
| Election dates | `sos.nebraska.gov/elections` page's own plain text ("Primary Election: ...") | HTML (plain-text regex) |

First real consumer of `XlsxTableParser`'s new `sheetIndex` parameter: one
workbook, two worksheets. Sheet 0 is the candidate roster — not just
statewide/federal/legislative races but ~90 distinct local special-district
entities (public power districts, natural resources districts, community
colleges, educational service units) with no county concept of their own,
so `County` stays null throughout even though real sub-state jurisdiction
exists (captured in `District` instead, alongside legislative/judicial
district numbers). `Office` only strips a leading `"For "` (`^For\s+`) —
statewide/federal/legislative rows have it ("For United States Senator"),
local entity names never do (some contain "For" mid-string, e.g. "Central
Community College For Board of Governors", which the anchored match leaves
untouched). `Mailing Address` and `Phone/Email` are each one workbook cell
holding 0–2 newline-separated values (street then city/state/zip; phone then
email, either possibly blank or absent) — split by regex match and by
presence of `@` respectively, rather than assuming a fixed line count.
Sheet 1 is judicial retention questions ("Shall Judge X be retained in
office?"), mapped to `StatewideProposedMeasures` since MeasureRow's shape
(a title, a jurisdiction, no opponent) fits a retention question much better
than CandidateRow does; `Jurisdiction` combines the office and district since
the model has no separate slot for either on `MeasureRow`, and `MeasureId` is
a slugified `office+district+judge` composite (no source-provided id exists).
Neither sheet carries an election date — both come from the elections page's
own plain text, matched with a simple `"Primary Election: <date>"` regex (no
day-of-week prefix, unlike HI's version of the same pattern). The workbook
itself is a live "currently filed" snapshot like MS's CSV, but with no
per-row date/type signal at all (unlike MS's Election/Primary/General
columns) — the whole file is attributed to Primary or General as one unit,
by comparing today against the scraped Primary date; retention questions are
always General (Nebraska judges are never on a primary ballot). Live-verified
2026-09-03 (after the May 12 primary had passed): 521 candidates, all
correctly landed in General, plus 48 retention questions. A real bug was
found via output inspection here too — `ResidentialCity` was written in the
mapper's own doc comment but the actual field assignment was missing from the
object initializer; fixed, with a named regression test. A third sheet
(petition-candidate filing status, including rejected/missed-deadline
attempts) exists in the same workbook but isn't attempted — candidates who
actually qualified by petition already appear in sheet 0 with their real
`"By Petition"` party value, so the third sheet would add only noise. A
separate `State_Level_Contests_PR26.xlsx` file on the same page is contest
*structure* metadata (which offices appear on the ballot, statewide vs. by
county) with no candidate names at all — not a data source, not fetched. No
county directory; no local/county-administered ballot measures (not
published in this workbook).

### Vermont (`StateBallot.States.Vt`)

| Data | Source | Format |
| --- | --- | --- |
| Candidates (primary + general) | `outside.vermont.gov/.../{year}_{statewide_primary,general_election}_qualified_candidates.xlsx` | XLSX |
| Election dates | `sos.vermont.gov/elections/election-info-resources/candidates` page's own plain text ("Primary Election - Tuesday, August 11, 2026 \| General...") | HTML (plain-text regex) |

Onboarded right after CO, and a useful contrast with it: VT's candidate XLSX
URLs *are* year-templated and confirmed stable back to at least 2024 (both
2024 files still resolve), so unlike CO, back-filling a past year's candidate
roster actually works. What doesn't back-fill is the election dates - the
`sos.vermont.gov` candidates page they're linked from is evergreen (its own
URL has no year), so `VtElectionDateScraper` only succeeds when the page's
plain-text "Primary Election - Tuesday, August 11, 2026 | General Election -
Tuesday, November 3, 2026" line parses to the *requested* year; a mismatch
returns null rather than a wrong or invented date, and `VtCollector` surfaces
that as a clear "back-filling a past year's dates isn't supported by this
source" error rather than silently mapping candidates under someone else's
election date. No PDF involved at all here (contrast with CO's calendar) -
same plain-text-regex idiom NE already established for its own elections page.

Far richer export than CO's: sixteen columns including full mailing address,
town of residence, two separate phone columns, email, and website - the
second-most-complete state onboarded (behind only WY/MD's dedicated
per-candidate exports) despite being a fresh XLSX pick. `Day Time Phone` and
`Evening Phone` are both plain personal contact numbers, neither labeled a
campaign line the way WY's "Campaign Telephone" is, so `VtCandidateMapper`
coalesces them into the one generic `Phone` slot (day first) by hand rather
than mapping the second one to `CampaignPhone` and mislabeling it or silently
dropping it - not a plain `CandidateFieldMapper` passthrough, since
coalescing two columns isn't a single-column lookup.

`Contest`/`District Name` needed real design work: `District Name` is a
literal `"N/A"` for every statewide/at-large office (Governor, US House -
Vermont is a single at-large district, Attorney General, ...), a **compound**
county-abbreviation + number code for State Senator/Representative (`"ADD
1"`, `"CHI CT 1"`, `"BEN RUT"` - not a plain number), a real county name for
five specific county-elected row offices (State's Attorney, Sheriff, Probate
Judge, Assistant Judge, High Bailiff - all 14 VT counties confirmed present
for each), or a town name for Justice of the Peace (229 distinct towns) -
kept in `District` verbatim rather than `County`, since `CandidateRow` has no
town-level field and `County` specifically means county. The county-elected
offices are a fixed name list (`VtSelectors.CountyLevelOffices`), the same
office-name-gated approach CO's mapper uses for its own County Court rows.

The State Senator/Representative compound codes surfaced a real, shared Core
bug: `OcdDivisionId.HasDistrict` previously matched on "district value
contains any digit anywhere," so `"ADD 1"`, `"BEN 1"`, `"CAL 1"`, etc. would
all digit-extract down to the same district `"1"` and produce the *same*
wrong `sldu:1` OCD id for every county's own district 1 - a silently wrong
answer, not just a missing one, and worse than CO's earlier gap because nothing
about the output would look obviously off without knowing VT's real district
naming scheme. Fixed narrowly in Core: `HasDistrict` now requires the whole
(trimmed) value be numeric (`^\d+$`) before treating it as a plain district
id; a value that merely contains a digit falls through to the caller's normal
not-a-recognized-plain-district handling (the same statewide-fallback path
CO's RTD single-letter subdistrict codes already exercise) rather than
returning a specific wrong district. No other onboarded state's District
values happened to exercise the gap (WY/NC/CO/MD's are all bare, already-split
numbers), confirmed by checking every state's own mapper tests before the
change; all pre-existing tests across the solution still pass unchanged.

Live-verified 2026-09-03: 2,860 candidates across the two 2026 elections (241
primary, 2,619 general - VT's per-town Justice of the Peace race alone
contributes hundreds of rows), correct OCD ids for statewide and
county-elected offices, State Senator/Representative correctly falling back
to the statewide OCD id rather than a wrong specific one, 935 rows with a
populated `Phone`. The primary XLSX's raw underlying worksheet XML carries an
entirely blank formatting row immediately before the real header row (visible
when inspecting the sheet's raw XML directly) - harmless in practice, since
both `XlsxTableParser`'s header-row detection and `VtCollector`'s own guard
(every genuine row has a non-blank `Contest`, mirroring CO's non-blank-Office
guard against its "End of Data" sentinel) already produced the exact expected
241-row count with no manual intervention needed, but worth noting as a
"looked fragile on paper, verified fine in practice" case for whoever
inspects this export's raw XML next and worries about the same thing. No
county directory; no measures attempted (VT's own ballot-measure/article
process is entirely town-level Town Meeting business, out of this pipeline's
current scope).

### Colorado (`StateBallot.States.Co`)

| Data | Source | Format |
| --- | --- | --- |
| Candidates (primary + general) | `sos.state.co.us/pubs/elections/vote/{primary,general}Candidates.html` → linked `.xlsx` | HTML → XLSX |
| Election dates | `sos.state.co.us/pubs/elections/calendars/{year}ElectionCalendar.pdf` | PDF (PdfPig, same library as CA) |

Onboarding first ruled out the actual RI request that started this session:
`vote.sos.ri.gov` sits behind a genuine Cloudflare *interactive JS challenge*
(`cf-mitigated: challenge` on every request, no header combination gets past
it) rather than the simple header-sniffing check TX's Cloudflare rule uses —
fundamentally unfetchable by this repo's plain-`HttpClient` `HttpFetcher`, so
RI was parked and CO picked instead after a handful of other candidates
(`vote.ri.gov`, `candidates.sos.mn.gov`, `elections.wi.gov`) turned out to sit
behind the same class of interactive challenge (Cloudflare or Radware).

Both candidate-list pages are static HTML with a linked "Excel version
(XLSX)" file — no bot-blocking at all — but neither the page nor the XLSX
filename is year-parameterized (the URL is always `generalCandidates.html`/
`primaryCandidates.html`, reflecting whichever cycle is current; the linked
file's own name additionally carries an "Official"/"Unofficial" suffix that
shifts partway through a cycle as lists get certified). `CoCollector`
therefore discovers the XLSX's href from the page rather than templating the
filename, and cross-checks the page's own "20NN Primary/General Election ...
Candidate List" heading text against the requested year before trusting the
data — a mismatch (e.g. requesting a past year this source no longer serves)
is recorded as a `Gap` + `PendingElection`, not silently mapped under the
wrong year. Back-filling a year other than the current cycle isn't supported
by this source at all.

Neither the candidate pages nor the XLSX carry an election date — only a
year-templated statutory election calendar PDF does, whose first page has a
plain two-line header ("Primary Election: June 30, 2026" / "General Election:
November 3, 2026"), extracted via `PdfPig`'s `ContentOrderTextExtractor`
(same library CA already depends on) rather than the multi-page
candidate-listing parse CA's own `CertifiedListPdfParser` does.

v1 scope is candidates only: the XLSX has exactly five columns (Candidate
Name, Office, District, Party, Write In?) — no filing date, address, email,
phone, or source-provided candidate id at all, the sparsest export onboarded
so far alongside WY's. The `Write In?` flag is folded into `Status` as a
literal `"Write-in"` string (same idiom WY uses for its withdrawal-date
signal) rather than inventing a new `CandidateRow` field for one state's
Y/N column. The `District` column is itself overloaded three ways depending
on office — `"State"`/`"Statewide"` (casing differs between the two files)
for statewide offices, a bare number for congressional/legislative/judicial
seats, or a bare county name for County Court/Associate County Court races
specifically (the *only* offices whose "district" value is actually a
county) — `CoCandidateMapper.SplitDistrict` classifies by office name rather
than by shape alone, since RTD Board of Directors uses single-letter
subdistrict codes (`"B"`, `"C"`, ...) that would otherwise look just as
non-numeric as a county name.

Two real bugs surfaced only by running the collector against the live site
and inspecting output field-by-field (2026-09-03), both fixed here:
`OcdDivisionId`'s office-name matching (`IsUsHouse`/`IsStateHouse` in Core,
shared by every state) didn't recognize CO's own office strings verbatim —
`"US House of Representatives"` and `"State House of Representatives"` —
so every congressional and state-house candidate silently got a null OCD id
despite having a real numeric district; both patterns extended narrowly
(prefix/substring-gated so the federal and state chambers still can't cross-
match each other) rather than special-cased in CO's own mapper, since the
matcher is shared Core code other states benefit from too. Separately, both
XLSX files end with a literal `"End of Data"`/`"End of data"` sentinel footer
row (blank Office/District/Party) that was getting ingested as a bogus
662nd/664th "candidate" until `CoCollector` started requiring a non-blank
`Office` column, the one column every genuine row always has. Live-verified
2026-09-03: 662 candidates across the two 2026 elections (251 primary, 411
general), no county directory, no measures attempted.

### Virginia (`StateBallot.States.Va`)

| Data | Source | Format |
| --- | --- | --- |
| Candidates (general only) | `elections.virginia.gov/casting-a-ballot/candidate-list/` → linked `.xlsx` | HTML → XLSX |
| Election dates | The candidate list page's own `<title>` (e.g. "Virginia Dept. of Elections: November 3, 2026 Gen Elect All Offices") | HTML |

The messiest source onboarded so far. `elections.virginia.gov`'s
`previous-candidate-lists` archive shows why: dozens of hand-typed slugs and
revision-dated filenames (`2026-November-All-Offices-Candidate-List-(rev-9-3-2026).xlsx`),
primaries split into up to four separate files per cycle (Democratic/
Republican × federal/local), one-off special-election lists with no shared
naming convention at all. Nothing here is year-templatable - not the index
page (evergreen, current-cycle-only), not any individual election page, not
any XLSX filename - so `VaCollector` discovers everything by scraping: the
index page for the current cycle's `"20NN ... All Offices Candidate List"`
link (loosely matched - only the leading year and trailing "All Offices
Candidate List" phrase are load-bearing, tolerant of the exact wording
drifting), then that election page for its own `<title>` (the only place the
date is published - the XLSX never carries one) and its linked XLSX. **v1
scope is the general election only** - unlike every other multi-file state
onboarded so far, no single reliably-discoverable "current primary" page
exists to generalize the same two-hop discovery to; a primary is genuinely
out of scope, not just unattempted.

The export's own shape needed real design work, distinct from every prior
XLSX-backed state: it's not one row per candidate, but one row per
**(candidate, locality)** - every federal/statewide race is repeated once for
each of Virginia's ~133 localities it appears on the ballot in (confirmed
directly: US Senate's own two major candidates are 134 rows each). Virginia
lists no state legislative races in an even-numbered year at all (Senate of
Virginia and House of Delegates are both odd-year-cycle offices), so 2026's
file holds only federal (US Senate, US House) races repeated this way plus a
long tail of genuinely local single-locality races (mostly small-town
mayor/council seats) - `VaCandidateMapper.ToCandidateRow` maps every row
as-is first (one CandidateRow per source row, exactly like every other
state's mapper), then a second pass, `MergeDuplicateLocalities`, groups by
(Office, District, Party, CandidateName) - the closest thing to a stable
per-candidacy key this source offers, since it has no source-provided
candidate id - and either collapses a federal/statewide race's duplicates to
one row (`VaSelectors.WideOffices` is the closed set: only `"Member, United
States Senate"` and `"Member, House of Representatives"` are live-verified in
this cycle; `"Senate of Virginia"`/`"House of Delegates"` are included
defensively from VA's documented naming convention but are flagged
NOT-live-verified in a doc comment - whoever revisits VA in an odd year
should confirm both against a real export) or, for a genuinely local race,
joins every distinct locality it was seen under with `"; "` - VA has real
multi-locality local races (confirmed: 45 groups, e.g. the Town of Belle
Haven straddles Accomack and Northampton counties), the first state onboarded
where the README's stated multi-county convention was actually exercised by
live data rather than just written down defensively.

Richer than CO's export, on par with VT's: full campaign address (two line
slots, joined into `MailingAddressLine`'s one slot rather than dropping the
second), email, website, and an explicitly-labeled `"Campaign Phone"` column
(mapped to `CampaignPhone`, not `Phone` - same idiom as WY's `"Campaign
Telephone"`; VA's export has no generic phone column at all, so `Phone`
stays null throughout). `Incumbent` is a real `"Yes"/"No"` column, parsed to
a real bool - the first state onboarded during this phase where incumbency
is actually published (every prior CSV/XLSX state left it null). District
text needed one more normalization VT/CO didn't: US House rows read `"2nd
District"` (an ordinal, not a bare number) - stripped to `"2"` before
`OcdDivisionId` ever sees it, matching the bare-number convention every
recognized office type in Core already expects.

A live, field-by-field-verified real bug came out of this state, in already-
shared Core code touched during CO's onboarding: `OcdDivisionId.IsStateHouse`
had been loosened there to match a bare `"house of representatives"` phrase
whenever no `"US"`/`"united states"` qualifier was present, reasoning (at the
time, without VA's data to check against) that a state's own chamber would
never say "House of Representatives" without *some* federal-sounding
qualifier ruling it out. VA's own federal seat is titled exactly `"Member,
House of Representatives"` - no qualifier at all - so every VA US House
candidate was silently landing on a wrong `sldl:N` (state house) OCD id
instead of the correct `cd:N`. Fixed by inverting the discriminator: `"House
of Representatives"` is federal *by default* now (`IsUsHouse`), and only an
explicit `"state house of representatives"` phrase (CO's own wording, still
exact-matched) routes to `IsStateHouse` instead - a positive signal for
state-ness rather than the previous absence-of-federal-markers heuristic.
Full solution's tests re-run clean after the fix (no other onboarded state's
office titles exercised the old, wrong branch). Independent-city localities
(e.g. `"ALEXANDRIA CITY"`) are still passed through `OcdDivisionId.County()`
unchanged - it slugifies them as `county:alexandria_city`, which is not the
real OCD taxonomy for a Virginia independent city (a `place:` type in the
canonical scheme) but is at least attributed to the right named place rather
than silently dropped or merged elsewhere; flagged here as a known
imprecision rather than guessed at further. Live-verified 2026-09-03: 1,322
real candidacies out of 2,015 raw rows (40 statewide/federal, 1,282 local, 43
of those spanning more than one locality), zero duplicate (Office, District,
Party, CandidateName) keys remaining after the merge pass. No county
directory; no measures attempted (the same index lists a separate "Proposed
Constitutional Amendments and Local Referendums" page, not attempted here).

### New Mexico (`StateBallot.States.Nm`)

| Data | Source | Format |
| --- | --- | --- |
| Candidates + judicial retention (general only) | `candidateportal.servis.sos.state.nm.us/CandidateList.aspx?eid=…`, "Excel (xls)" export button | HTML table wearing an `.xls` label |
| Election dates | `sos.nm.gov/voting-and-elections/view-all-elections/` page's own plain text | HTML |

Picked after independently curl-verifying it was a false positive in the
original "flagged blocked" research (wide open, no UA needed at all -
`configDrivenPipelinePlan.md` has the full triage). Onboarding surfaced two
genuinely new problems no prior state had, both handled with new,
reusable Core additions rather than one-off code:

**No discoverable election-id index.** The candidate portal (a
Telerik-grid-backed ASP.NET WebForms app, `?eid=NNNN` in every URL) has no
election-selector dropdown, no link to any sibling election, nothing - each
`eid` is a fully opaque integer good for exactly one election, and the one
nominally-current link on `sos.nm.gov`'s own site turned out to be a dead
link to a 2021 election (its real 2026 eids were only findable via a web
search, which is not something a collector can depend on repeating). HI hit a
milder version of this same problem and solved it by reading its *own* page's
election dropdown; NM's page doesn't have one to read. So this is the first
state whose election id is hand-maintained config instead of discovered at
all: `data/input/nm/election_ids.json` (loaded via a new `DataPaths.
ElectionIdsPath`, using the same generic `LookupTableLoader` the field-map
config already uses) - a human re-derives the value once per cycle and the
collector fails loudly, naming the file, if the entry it needs is missing.
**v1 scope is the general election only** for a related but distinct reason:
even though the *candidate* data for NM's primary is sitting at its own,
still-fetchable `eid`, this pipeline could find no live source anywhere for
the primary's *exact date* once the primary itself had passed - the
`view-all-elections` page only ever states the next *upcoming* election, and
several search-indexed NM SOS pages that did once state the primary's date
(`/2026-primary-election-day/`, `/2026-primary-election-results/`) now 404.
Rather than hardcode a well-known public date against this project's
otherwise-universal "never hardcode, always scrape" rule, the primary was
left out instead - the same trade-off VA made for its own primary, for a
different underlying reason.

**The CSV export corrupts itself; the "Excel" export doesn't (because it
isn't one).** The same page offers "Text File (csv)" and "Excel (xls)"
exports side by side. The CSV is genuinely broken for real data: some fields
(a name suffix like `", SR"`, a street address like `"2003 Southern Blvd.
SE, Box 102-59"`) contain a literal, unescaped comma the exporter never
quotes, silently shifting every column after it on that row - confirmed live,
69 of 524 rows (~13%) corrupted this way, `Party` cells ending up holding
fragments like `"SR"` or `"CHAVEZ"`. The "Excel (xls)" option turned out to
be a plain HTML `<table>` served with an `.xls` filename and
`content-type: application/vnd.ms-excel` - a well-known Telerik RadGrid trick
that most spreadsheet software still opens correctly, but is not a real
binary or OOXML spreadsheet at all (ClosedXML, Core's only spreadsheet
reader, would reject it outright). Reading it as what it actually is - HTML -
sidesteps the comma problem entirely (a `<td>` has no delimiter to escape).
This is the first state to need an HTML-table reader as a *generic* format
rather than page-specific scraping, so it became a new sibling to
`DelimitedTableParser`/`XlsxTableParser`: `HtmlTableParser` (Core, new
AngleSharp dependency there). Its own wrinkle: some of NM's own contest names
embed a real `<br />` inside one cell (`"Judicial Retention<br />Judge of
the Metropolitan Court DIVISION 2"`, distinguishing multiple simultaneous
retention votes) - plain `TextContent` would concatenate the two halves with
no separator at all (a `<br>` carries no text of its own), so the parser
reads each `<br>` as a space explicitly.

The export is also the first WebForms consumer needing more than a bare
button click: the target format is itself a `<select>` the page must be told
to change before the export button is "clicked", so `WebFormsPostback.
ClickButtonAsync` gained an optional `extraFields` parameter (every existing
HI/MS call site omits it, unaffected).

Field/district design, all live-verified against the real 524-row export:
`Filing County` is the office's true jurisdiction only for a fixed set of
whole-or-sub-divided-county offices (`NmSelectors.CountyScopedOffices`:
County Assessor/Clerk/Commissioner/Sheriff/Treasurer, Probate Judge,
Magistrate Judge, Judge of the Metropolitan Court) - for everything else
(State Senator/Representative, US Representative, District Court Judge,
Municipal Judge, Public Education Commissioner) it's merely where that
particular candidate personally filed, not the race's real scope, so it's
left null there even when populated (a District Court Judge's judicial
district spans several counties; attributing it to just one would be a
"never invented" violation, not a convenience). `District` cells shaped like
"DISTRICT N" / "COUNTY COMMISSION DISTRICT N" normalize to a bare number
(feeding `OcdDivisionId`'s `sldu`/`sldl`/`cd` mapping correctly, confirmed
live); "DIVISION N", a named judicial district, or a municipal district are
kept verbatim, since neither is a plain numbered legislative seat. Judicial
retention rows (`Contest` starting with "Judicial Retention") map to
`StatewideProposedMeasures` via a dedicated `NmRetentionMapper`, same
modeling choice as NE's own retention sheet (a yes/no vote on one named
incumbent, no party, no opponent) - the seat descriptor, when the contest
names one, becomes both part of the constructed `Title` ("Shall {name} be
retained in office as {seat}?") and `Jurisdiction`, since `MeasureRow` has no
separate slot for it. NM is also the first state whose candidate export
folds a two-person joint ticket (Governor and Lieutenant Governor, on a
"Republican Party" ballot line) across First/Middle/Last Name in an unusual
way (`"GREGGORY D HULL"` / `"AND"` / `"DAVID M GALLEGOS"`) - the ordinary
name-join logic renders it correctly with no special-casing needed. Live-
verified 2026-09-09: 481 candidates + 43 judicial retention questions from
524 raw rows (zero dropped), correct `cd`/`sldu`/`sldl` OCD ids, correct
county attribution across every office type tested, the exact
comma-in-address case that broke the CSV export confirmed intact in the
real output. No county directory; local/party-run elections (a separate
`eid`, "2026 Local Election Contest/Candidate List") not attempted.

### South Carolina (`StateBallot.States.Sc`)

| Data | Source | Format |
| --- | --- | --- |
| Candidates (primary + general) | `vrems.scvotes.sc.gov/Candidate/CandidateSearch/` (POST `ElectionId`) | HTML table |
| Elections + dates | `vrems.scvotes.sc.gov/Candidate/GetElections?electionType=General&year=…` | JSON |

Picked next in the user's own stated sequence (OR, SC, SD, MT) after Oregon
turned out blocked (see its entry in `configDrivenPipelinePlan.md`'s
"Confirmed blocked" table - found while researching SC, not before). SC's
own portal (VREMS, "Voter Registration and Election Management System")
turned out to be the simplest state onboarded in a long while, on two
fronts at once neither of which any prior state combined:

**One API call gives elections *and* dates together.** Unlike every
XLSX/HTML-table state onboarded before it (CO, VT, VA, NM), which all needed
a separate scrape just to find the actual election date, SC's own
`GetElections?electionType=General&year=2026` returns a JSON array with
each election's id, name, *and* real `electionDate` in one response -
confirmed live: `[{"electionId":"22596","electionName":"Statewide General
Election","electionDate":"2026-11-03T00:00:00",...}, {"electionId":"22598",
"electionName":"Statewide Primary","electionDate":"2026-06-09T00:00:00",...},
{"electionId":"22760","electionName":"US Senate Special Republican
Primary",...}]` - note the "General" *kind* (one of three: General/Special/
Local, from the search page's own dropdown) returns the statewide primary,
the statewide general, *and* any same-cycle special election together; the
collector matches by each election's own stable `electionName` ("Statewide
General Election" / "Statewide Primary") rather than assuming a fixed
position or count, so the special election here is simply never matched and
never collected - no special-casing needed, and no vulnerability to a future
cycle adding a second special election.

**The candidate search needs no session state at all.** Reached from a
browser, the flow looks stateful: a `SelectElection` page needs a real
antiforgery token to submit, redirects to `CandidateSearch?electionId=N`,
which is a big form (Office/County/Party/Status filters) that AJAX-POSTs to
`/Candidate/CandidateSearch/` and swaps in the returned HTML fragment as a
DataTable. Tested directly: a bare `POST /Candidate/CandidateSearch/` with
only `ElectionId` set - no cookies, no antiforgery token, no other form
field - returns the exact same 634KB/1,440-row fragment a full browser
session gets, confirmed identical whether sent as `multipart/form-data` (as
the page's own jQuery does) or plain `application/x-www-form-urlencoded`
(what `HttpFetcher.PostFormAsync` already sends) - so the whole
`SelectElection` page is never visited by this collector at all. The
response is the same "HTML table serving as the real data format" shape NM's
onboarding introduced `HtmlTableParser` for, reused here as-is with zero
changes needed.

Also a second confirmed instance of the exact lesson NM's own onboarding
taught: the research CSV describes SC as split-by-party (two rows,
Democratic/Republican, same URL) - `ScCollector` never sets a party filter
at all, and the live response already contains both parties (and every
minor party - `United Citizens`, `Workers`, `Alliance`, ... - confirmed
across 12 distinct party values in the general alone) in one unified table.
`SourceEntry`'s planned party attribute (see the plan doc's "What's still
not started") is looking less and less needed the more real per-state
mechanisms get checked, rather than trusted from the research spreadsheet's
row shape alone.

Field design: the export itself is sparse - Office, Associated Counties,
Name on Ballot, Running Mate, Party, Location of Filing, Candidate Status,
no contact info or filing date at all (same tier as WY/CO). District isn't
its own column - it's embedded in the Office cell's own text, and SC's
46 counties clearly haven't standardized how they name their own local
seats: `"U.S. House of Representatives, District 5"` and `"State House of
Representatives, District 100"` both cleanly split on a `", District N"`
suffix (confirmed feeding `OcdDivisionId`'s `cd`/`sldl` mapping correctly
live), but `"County Council District 3"` (no comma), `"Solicitor Circuit
12"` (no "District" word at all), and `"Clover School District Trustee
Seat 1"` are all real, different shapes the same underlying concept takes
elsewhere in the same export - `ScCandidateMapper` only splits the one
clean, confirmed-consistent comma shape and leaves everything else verbatim
in `Office` with a null `District`, rather than guessing at 336 distinct
office strings' worth of local naming conventions. `Associated Counties` is
its own column, already comma-joined by the source for a genuinely
multi-county race (a judicial circuit, a legislative district spanning two
counties) - re-joined with `"; "` to match this project's own convention
rather than left in the source's delimiter; unlike NM's `Filing County`, no
office-type classification was needed here, since the source's own column
is already the *correct* jurisdiction signal for every office live-checked,
not merely where a candidate personally filed. Live-verified 2026-09-10:
1,823 candidates across the two 2026 elections (383 primary, 1,440
general), zero exact-duplicate rows, correct OCD ids for federal/state
legislative districts, correct multi-county joins including a legislative
district that is itself multi-county. No county directory; local/off-cycle
elections (SC's own separate "Local" election kind) and the parallel
referendum search the same portal offers are not attempted.

### South Dakota (`StateBallot.States.Sd`)

| Data | Source | Format |
| --- | --- | --- |
| Candidates (primary + general) | `vip.sdsos.gov/candidatelist.aspx?eid=…`, "Export to CSV" RadGrid toolbar button | CSV |
| Election dates | `sdsos.gov/.../general-information/default.aspx` → current `*-candidate-calendar.aspx` link's own plain text | HTML |

Next in the user's own stated sequence (OR, SC, SD, MT) after SC. VIP (the
SD SOS's "Voter Information Portal") shares NM's exact election-id problem -
no dropdown, no sibling links, nothing discoverable at all, the id is
opaque and has to be found by a human (confirmed live: `eid=773` is the 2026
primary, `774` the general, found only via a web search, not any page on the
site itself) - so `data/input/sd/election_ids.json` hand-maintains both,
same mechanism NM's onboarding introduced. Where SD differs from NM in a way
that actually matters: NM's SOS site removes a *past* election's date once
it's over, which is why NM's v1 scope stops at the general; SD's own
election-calendar page keeps *both* the primary's and the general's real
date in plain text long after the primary has passed (confirmed live,
2026-09-10, four months post-primary: `"June 2, 2026 Primary Election"` and
`"November 3, 2026 General Election"` both still present, verbatim, on the
same evergreen page), so SD's v1 scope covers both elections - the ids are
the only hand-maintained piece here, not the dates. The calendar page's own
URL is itself discovered fresh each run (a regex for `*-candidate-
calendar.aspx` against the stable `general-information/default.aspx`
page's current links) rather than templated by year, since its path embeds
a year-named folder (`.../2026 Election Information/2026-candidate-
calendar.aspx`) whose exact folder-naming convention across future cycles
isn't something this onboarding pass could confirm. The date/label text
itself sits in separate sibling `<li>` elements with real markup between
them (`<li><strong>June 2, 2026</strong></li> ... <ul><li>Primary Election`)
- unlike NE's plain-text page, a bare regex against the raw HTML can't
bridge that gap, so this is the first plain-text date scrape to actually
need AngleSharp's parsed `Body.TextContent` (already precedented for VA's
`<title>` read, just applied here to a whole page) before the regex ever runs.

**A second, different kind of WebForms button than HI's.** SD's own
Telerik RadGrid "Export to CSV" toolbar button renders as
`<input type="button" ... onclick="javascript:__doPostBack('ctl00$Main
Content$grdCandidates$ctl00$ctl02$ctl00$ExportToCsvButton','')">` - not a
genuine `<input type="submit">` the way HI's identically-labeled button
happens to be, so `WebFormsPostback.ClickButtonAsync`'s "add one named
field" replay doesn't apply (there is no `buttonName=value` field to add;
ASP.NET dispatches a `__doPostBack`-triggered control by inspecting the
already-present, normally-blank `__EVENTTARGET`/`__EVENTARGUMENT` hidden
fields instead). Verified server-side before writing any C#, the same way
NM's postback mechanism was: a bare `HttpFetcher.PostFormAsync` with
`__EVENTTARGET` set to that control's full dotted name and the page's own
other hidden fields carried through returns the identical CSV a real click
would (296KB, 745 rows, confirmed byte-for-byte against a live-session
capture) - no headless browser needed after all, just the right two fields.
Generalized into a new Core sibling, `WebFormsPostback.TriggerPostbackAsync`,
rather than special-cased in SD's own collector, since any other Telerik-
grid state (there will likely be more) could hit the same button shape.

**The CSV itself, unlike NM's, is genuinely well-formed** (RFC4180 quoting,
including a doubled-quote-escaped nickname - `"Nicole ""Nikki"" Gronli"` -
parsed correctly by the existing CsvHelper-backed `DelimitedTableParser`
with no changes), so no `HtmlTableParser` detour was needed here despite
both portals being the same underlying Telerik product. The one CSV
housekeeping detail: the export carries a UTF-8 BOM that needs stripping
before parsing (`DelimitedTableParser` doesn't do this itself, so
`SdCollector` does after fetching).

Field design needed real live verification, not just a first look: the
export's single "District/County" column is genuinely overloaded and
*inconsistently formatted even within one office*. County Commissioner's own
sub-district shows up as `"Aurora-1"` in most counties, `"Deuel - District
1"` in a few (a spelled-out label word most counties omit), and `"Lyman -
District 3-4-5"` in one (a *compound* multi-sub-district seat with no single
bare number to extract at all). `SdCandidateMapper` first tried a narrow
`"{county}-{n}"` pattern; a first live write caught two real gaps output
inspection surfaced, not the initial design: `"Deuel - District 1"` fell
through un-split into a garbled `County` value, and two more real offices -
`"County Finance Officer"` and the party-organizational `"Delegates to
State Convention"`/`"Precinct Committeeman"`/`"Precinct Committeewoman"` (the
latter two sub-divided further still, by *precinct* rather than district,
with their own third separator variant, `"Aurora - Precinct-2"`) - were
missing from the county-office allowlist entirely, confirmed by
cross-checking every office's values against the real 66-county set derived
from `Sheriff`'s own (always-bare-county) column. Both fixed: the regex
generalized to `^(?<county>[^-]+?)\s*-\s*(?:\w+[\s-]*)?0*(?<n>\d+)$` (a
generic optional label word, not hardcoded to "District"), which correctly
splits all three real separator shapes while still *failing to match* -
deliberately - the one compound case (`"1-2"`/`"3-4-5"` isn't purely
numeric, so `"Lyman - District 3-4-5"` stays whole in `County` rather than
guessing at a wrong single sub-district); the allowlist grew to include the
two missed offices. `Mailing Address` is one undelimited blob (`"2604 S
Kierra Ct Sioux Falls SD 57106-5008"` - no comma anywhere between street and
city) - only the trailing `state`/`zip` are reliably separable off the end
(743 of 745 general-election rows matched cleanly; the 2 that didn't are
genuine source typos - a `"572.34"` zip, a bare trailing `"-"` - left whole
with a null state/zip rather than guessed at); street and city stay merged
in `MailingAddressLine` since there's no delimiter or lookup table to split
them further. No phone/email/website/status columns exist in this export at
all - sparser even than SC's. Live-verified 2026-09-10 (after the fixes):
2,977 candidates across the two 2026 elections (2,232 primary - SD's own
June primary date is also when most nonpartisan local/municipal races and
party-organizational precinct positions are decided, which is why the
primary substantially outnumbers the general here, confirmed a real count
matching the live source exactly, not a parsing artifact - 745 general),
zero exact-duplicate rows, zero remaining county-attribution gaps after a
second full cross-check against all 745+2232 raw rows, correct `sldu`/`sldl`
OCD ids. No county directory; no measures attempted.

### Montana (`StateBallot.States.Mt`)

| Data | Source | Format |
| --- | --- | --- |
| Candidates + elections + dates (primary + general) | `candidatefiling.mt.gov/candidatefiling/CandidateList.aspx?e=…`, "Export to CSV" RadGrid toolbar button | CSV |

Last in the user's own stated sequence (OR, SC, SD, MT). MT is the third
state onboarded on the same underlying Telerik RadGrid election-management
platform (after NM and SD), and by far the smoothest of the three to
discover: the bare `CandidateList.aspx` URL (no election id at all) `302`s
to whatever election is presently current, and *that* redirected page's own
`ddlElection` dropdown lists **every** election - both the primary and the
general, each with its own id, name, *and* real date all embedded directly
in one option's text (confirmed live: `"FEDERAL PRIMARY 2026 (06/02/2026)
(Primary)"`) - so a single fetch (the redirect followed transparently by
`HttpFetcher`'s default `HttpClient` behavior) discovers everything this
collector needs. No hand-maintained `election_ids.json` at all, unlike NM
and SD - the one thing distinguishing this portal from those two isn't the
underlying software (identical Telerik grid, identical `type="button"` +
`__doPostBack` export toolbar button, so `WebFormsPostback.
TriggerPostbackAsync` - built for SD - applied here completely unchanged),
it's that MT's own page happens to expose a real election-selector
dropdown where NM's and SD's don't.

**No county-level offices exist in this export at all** - confirmed by
checking every one of the "District Type" column's values (`Statewide`,
`Congressional`, `House`, `Senate`, `Judicial`, `Public Service Commission`,
`Supreme Court Justice`) across both the 2026 primary and general exports;
Montana's county races are filed with county clerks separately, not with
the Secretary of State, matching the original research notes' own caveat
("comprehensive county-level candidate data is not standardized statewide").
`CandidateRow.County` stays null throughout - simplest scope of any
XLSX/CSV-backed state onboarded so far in that one respect.

**Office/district splitting needed a per-`District Type` dispatch, not one
regex** - confirmed live that the actual district number lives in a
different place depending on race type, in a way no single pattern against
the `Race` column could cover on its own: `Race` is the bare `"UNITED STATES
REPRESENTATIVE"` for *both* Montana congressional districts (the number
lives only in the separate `District` column, `"1ST CONGRESSIONAL"`/`"2ND
CONGRESSIONAL"`) - the opposite problem VA's own US House rows had, where
`Race` was the one column that *did* carry it; a `"#N"` seat suffix for
Supreme Court Justice (a numbered seat, not a geographic district -
`"SUPREME COURT JUSTICE #4"`); a `"DISTRICT N, DEPT M[ UNEXPIRED]"` suffix
worth keeping whole for District Court Judge, since more than one judge can
share the same numbered judicial district and dropping the department (or
an unexpired-term flag) would silently conflate two different seats; and a
plain trailing `"[,] DISTRICT N"` for House/Senate/Public Service
Commission (the comma being present only for the latter -
`"PUBLIC SERVICE COMMISSIONER, DISTRICT 1"` vs `"STATE SENATOR DISTRICT
1"`). `MtCandidateMapper.SplitOfficeDistrict` switches on `District Type`
rather than trying to unify these into one pattern.

Richest contact-field export of any Telerik-platform state so far:
`Email/Web Address` is one cell with an embedded `"<br />"` separator
(confirmed live, exactly one literal variant across every row checked) -
email always first, a website second when present, told apart from that
column's own placeholder values (`"Not Provided"`, `"WRITE-IN"`) by whether
the second part contains a literal `.` at all rather than hardcoding those
specific sentinel strings. `Mailing Address` is a comma-joined
`"street[, city][, state][, zip]"` cell parsed right-to-left, since city
and/or state are each sometimes dropped entirely by the source (confirmed
live: `"3405 NORTH AVE W, MISSOULA, 59804"` has no state;
`"31 WAVING GRASS WAY, MT, 59912"` has no city) - a city is only ever
extracted when a part is actually left over after zip and state are both
accounted for, so the ambiguous one-part-remaining case (street with the
city dropped) doesn't get misread as a city with the street dropped; a
"never invented" call that has no way to be verified further from the
export alone. Live-verified 2026-09-13: 652 candidates across the two 2026
elections (380 primary, 272 general - both matching the raw CSV row counts
exactly), zero duplicate rows, correct `cd`/`sldu`/`sldl` OCD ids including
the Congressional-column-derived one, the no-city mailing-address edge case
confirmed handled correctly in real output rather than just in a unit test.
No county directory; no measures attempted.
