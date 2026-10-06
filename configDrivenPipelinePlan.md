# Config-Driven Pipeline: Plan

Clean rewrite of the working plan as of 2026-09-03, refreshed 2026-09-08. For
what's actually built and how it works, see
[`configDrivenPipelineInfo.md`](configDrivenPipelineInfo.md) — this doc is
forward-looking: what's done at a glance, and what's left. The messy,
narrative version of this plan (every decision, every dead end, kept for
historical detail) remains at
`/Users/alex/.claude/plans/in-anticipation-of-this-shiny-gizmo.md`.

## Goal

The long-term goal — explicitly deferred, not yet built — is a UI that lets
someone configure a new state's data sources and field mappings without
writing C#. Getting there requires two things this plan has been chipping
away at:

1. **Consolidation**: pull duplicated per-state logic (date parsing, address
   formatting, FIPS loading, empty-result guards, selector patterns) into
   shared, config-loadable primitives, so a config surface has something
   real to sit on top of.
2. **Coverage**: enough real states onboarded, across enough different
   source formats and fetch mechanisms, that the config surface is designed
   against real diversity rather than the first 4 states' happenstance.

## Status at a glance

| Phase | Status | What it did |
| --- | --- | --- |
| 0 — Core utility extraction + mapper convention | **Done** | Deduped whitespace/date/address/FIPS/empty-guard logic into Core; standardized the mapper-class seam across WA/CA/TX/WV; added test coverage where none existed (CA, WA). |
| 1 — Config-driven value tables | **Done** | `date_formats.json` (all states) and `election_type_names.json` (TX) — externalized, fail-loud or permissive per the field's semantics. |
| 2 — Selectors externalization | **Done** | CA and WA's CSS-selector/regex constants moved from compiled `static class` fields to `sealed class` instances loaded from `selectors.json`, with a `Default` compiled-in fallback for tests/seeding. |
| 3 — 50-state reality check + new-state onboarding | **In progress** | Research across all 50 states' real formats/fetch-blocking; format-parser infra (CSV, XLSX, HTML table) built; 13 new states onboarded (MD, NC, WY, HI, MS, NE, CO, VT, VA, NM, SC, SD, MT); remaining 34 states' bot-blocking status independently verified 2026-09-08/09/10 (see below). |

17 states implemented total: WA, CA, TX, WV (pre-existing) + MD, NC, WY, HI,
MS, NE, CO, VT, VA, NM, SC, SD, MT (added under Phase 3). Full technical
detail on all 17 is in `configDrivenPipelineInfo.md`.

## Phase 3 in detail

### What research changed about the plan

Early Phase 3 research (a 50-state format/fetch-blocking pass, originally
external and not persisted in this repo) showed the 4-state sample
(WA/CA/TX/WV) undersold real diversity. As of 2026-09-08 that research is
persisted at `.user/State Candidate Rosters in Digital Format, 2026 - All
States.csv` — a per-state, per-party breakdown of roster/measure formats,
direct URLs, and acquisition notes. It turned out to be strong on format
data (nearly every row has a real, specific format) but weak on bot-blocking
(its "Blocks Bots?" column is almost entirely blank — only 3 of 56 rows
(50 states + DC, plus 5 rows for states that split by party) carry an
explicit TRUE/FALSE; a few others have a bare `robots.txt` link
pasted in rather than a verdict). Every blocking conclusion in this doc is
from direct `curl` verification against the live site, not from that column.

- **Format**: CSV and XLSX are the biggest buckets by far, and the cheapest
  to add, since `CsvHelper` was already a dependency (used for output) and
  only one new library (ClosedXML) was needed for XLSX.
- **Bot-blocking was originally over-flagged, not under-flagged.** The
  earliest research pass (predating the CSV persisted above, whose own
  "Blocks Bots?" column was mostly left blank rather than filled in with the
  same rigor as its format columns) hinted at blocking for roughly 22 of 50
  states. Independent live `curl` verification (ongoing since MD/NC/WY/HI,
  completed for all 38 remaining states on 2026-09-08 — see
  "Remaining-state triage" below) found the real number much smaller: 9
  states are genuinely blocked, all by real bot infrastructure (Cloudflare,
  Akamai, Incapsula, or Radware) rather than anything state-specific. A bare
  `curl` 403 with no UA is essentially worthless as evidence either way —
  Mississippi, New Mexico, Oregon, South Carolina, South Dakota, Montana,
  Arizona, and New Hampshire all had to be checked with a real browser UA
  (and, for Mississippi, also the reverse — a tool-like UA) before their true
  status was known.
- **Party is sometimes a source-level split**, not a per-row field (AL, MA
  each publish separate URLs per party) — `SourceEntry` doesn't yet have a
  way to say "every row from this source is party X." (NM and SC both
  looked like they'd need this too from the research CSV alone, but each
  turned out to be an optional filter on one unified export/search once
  actually built - see item 3 under "What's still not started".)
- **Status/withdrawal is a real canonical-model gap** — fixed by adding
  `CandidateRow.Status` (raw passthrough, not normalized) during the Maryland
  onboarding, ahead of the states that actually needed it.
- **A genuinely-hard tier exists** (e.g. Georgia: session token + recaptcha)
  that no config schema fixes — worth keeping explicitly separate from "just
  needs more config" so it doesn't stall everything else.

### Format-parser infrastructure — done

`DelimitedTableParser` (CSV/TSV, CsvHelper-backed) and `XlsxTableParser`
(ClosedXML-backed) in Core, both returning the same
`List<Dictionary<string,string>>` shape. No format-registry/interface was
built — every state still dispatches to its own library directly, since no
call site has ever needed to pick a parser by a runtime format string.
Revisit if that stops being true.

### New-state onboarding — 13 done

MD, NC, WY, HI, MS, NE, CO, VT, VA, NM, SC, SD, MT. Each was researched live (direct
`curl`/browser checks against the real government site), designed, built
following the established file-per-concern convention (SourceConfig /
Selectors / Mapper / Collector / PublishSchedule), wired into the
catalog/CLI/solution, tested, and live-verified end-to-end before being
called done. Full per-state writeups are in `configDrivenPipelineInfo.md`.

### Remaining-state triage (34 states, verified 2026-09-08/09/10)

Every one of the 34 not-yet-implemented states now has a live-verified
bot-blocking status and a known format (from the CSV above, cross-checked
against the live site). Grouped by what's actually blocking progress. The
user's own stated onboarding sequence (OR, SC, SD, MT) is now complete - OR
turned out blocked (see below), the other three are done (see
`configDrivenPipelineInfo.md`).

**A verification-method gap, found 2026-09-10 (Oregon):** the 2026-09-08 pass
that called OR "open" only checked HTTP status + a non-trivial byte count -
enough to catch AR's silently-*empty* 200, not enough to catch a
substantial, legitimate-looking 200 that's still a bot-block page. OR's own
block page is real HTML with a real `<title>`, explanatory text, and contact
info, well over the "silently empty" bar, and would have passed that
earlier check. Every state's "open" verdict elsewhere in this table was
still eyeballed for real content (not just re-derived from this bug), but OR
itself has been moved down to "Confirmed blocked" below - see its entry
there for what was actually checked this time.

**Ready to build — confirmed open, format known (19 states):**

| State | Format | Notes |
| --- | --- | --- |
| Alabama | pdf (+ html, party sites) | Split by party; format is whatever each party happens to submit — inconsistent; no contact info in either. |
| Alaska | html | No downloadable file — pure scrape. Address/email/phone/website included. |
| Connecticut | pdf | CSV notes it as "sample ballots only"; the candidate-list page itself looks stale/unmaintained. |
| Delaware | html | — |
| District of Columbia | CSV, PDF, XLSX | Direct CSV export (`ActiveCandidates/Export?exportType=CSV`); no counties/districts to reconcile — cleanest of this batch. |
| Florida | tsv (.txt) | Dynamically generated per search query, not a static file — closer to an API than a download. |
| Iowa | pdf | Separate primary/general pages; county election-office links provided. |
| Illinois | tsv (.txt) | Updates every 15 minutes; counties have individual sites in various formats. |
| Indiana | html or xlsx | Two separate sources (a citizen-journalism site + the SOS site) to reconcile. |
| Kansas | html | County list has no websites attached. |
| Kentucky | xls | — |
| Massachusetts | html | Split by party, separate pages; published late (after June 2). |
| Maine | xlsx + pdf | Office-code key is a *separate* PDF from the XLSX — two-file join needed. |
| Missouri | html | Confirmed open both by the CSV (explicit FALSE) and by curl; one big page with candidates *and* measures together. |
| North Dakota | (unlisted in CSV; page loads live) | — |
| New Jersey | pdf ×2 | An official PDF (name/party/address) and an unofficial one (name/party/email) — need both. |
| Pennsylvania | html | — |
| Tennessee | xlsx, pdf | Separate file **per office type** — no single roster file. |
| Utah | html or xlsx | Has both an HTML table and a direct Excel download — take the XLSX. |

**Confirmed blocked — need a fetch-strategy tier this pipeline doesn't have yet (10 states):**

| State | Vendor / mechanism | Verified |
| --- | --- | --- |
| Michigan | Cloudflare `cf_clearance` cookie gate on every path (`mvic.sos.state.mi.us`) | 2026-09-03 — also has no statewide export at all as a separate, prior problem (SOS page is just links to county clerks). |
| Minnesota | Radware (`perfdrive.com`) — whole `mn.gov` domain redirects every request to a bot-check validator page | 2026-09-03 |
| Nevada | **Incapsula** (new vendor) — whole `nvsos.gov` domain returns a "Request unsuccessful, Incapsula incident ID..." wall | 2026-09-08 |
| New Hampshire | **Akamai** edge WAF (new vendor) — whole `sos.nh.gov` domain returns a generic Access Denied via `errors.edgesuite.net` | 2026-09-08 |
| New York | Cloudflare, explicit challenge, 403 | pre-2026-09-03 |
| Oklahoma | AWS ELB block, 403 | pre-2026-09-03 |
| Oregon | **F5 (`TSPD` cookies)** — the search-page *shell* (`orestar/CFSearchPage.do`) loads fine, but both data-yielding endpoints behind it (`orestar/cfFilings.do`, and the AJAX servlet `orestar/ajaxdataserver/getCandidateElectionByYear` that populates its election dropdown) return a real block page ("...our cyber-security service...has identified a problem") - a `200`, not a `403`, and non-trivial in size, so it passed the earlier, too-shallow "status + byte count" check on 2026-09-08. Confirmed blocked regardless of UA (browser/curl-like/none), with or without a warmed-up session cookie + matching Referer from the shell page first. | 2026-09-10 |
| Rhode Island | Cloudflare interactive JS challenge (`cf-mitigated: challenge`) on every path/header combo, including the `vote.ri.gov` alias | 2026-09-03 |
| Wisconsin | Cloudflare interactive JS challenge, same pattern as RI | 2026-09-03 |
| Arizona | Cloudflare interactive JS challenge (`apps.arizona.vote`) | 2026-09-08 |

All ten need the same missing piece — headless-browser rendering (or, for
Incapsula/Akamai/F5 specifically, possibly a different bypass than
Cloudflare's) — not state-specific work. See "What's still not started" below.

**Genuinely hard — not a blocking/config problem at all (4 states):**

- **Georgia** — session token + CAPTCHA on the actual form (json).
- **Louisiana** — two-call dedup required across all 64 parishes (html).
- **Ohio** — no master candidate list exists; only a sample-ballot lookup
  tool, county by county (html).
- **Arkansas** — real, well-shaped JSON export
  (`candidates.arkansas.gov/wp-json/metl/v1/all`), but blocked by
  TLS/HTTP-fingerprint scoring rather than an interactive challenge — a real
  headless Chromium gets full data back immediately, no header/cookie/UA
  trick via `curl` does. Deliberately deferred: this pipeline has no
  deployment target yet, and the datacenter IP range of whatever host
  eventually runs it would likely score worse than this dev machine does, so
  a headless-browser tier's viability here can't be confirmed until a real
  deployment target exists to test from.

**Still untriaged (1 state):**

- **Idaho** — real JSON API (`api-run.voteidaho.gov/api/FiledCandidates/SearchCandidates`),
  but it's a POST endpoint requiring a JSON search-filter payload in the
  body; a bare GET `curl` doesn't exercise it meaningfully. Needs a proper
  payload replay (like TX's or WV's undocumented-API pattern) to know its
  real status.

## What's still not started

Carried forward from Phase 3's original architecture recommendations, none
begun yet:

1. **Fetch-strategy tiering as a first-class, config-driven axis.**
   `HttpFetcher.AddDefaultHeader` (TX, and MS's UA-inversion variant) and
   `WebFormsPostback` (HI, MS) are the first real pieces of this, but neither
   is exposed as per-source config yet — both are still hardcoded into their
   state's collector. The next tier up, headless-browser rendering, now has
   ten real customers (see "Confirmed blocked" above) but is gated on a
   real deployment target existing to test IP-reputation against, not on
   anything left to design.
2. **Field-mapping UI/engine.** The original "hard problem," reprioritized
   rather than deferred once 50-state diversity became concrete — should
   consume the primitives Phases 0-2 already extracted (date formats,
   lookup tables, selectors) as the configurable "value transformers" a
   mapping UI would expose per field. The plain-passthrough slice of this is
   now done (`CandidateFieldMapper` + `candidate_field_map.json`, one per
   CSV/XLSX-backed state — see `configDrivenPipelineInfo.md`); what's left is
   the *transform*-needing fields it deliberately doesn't cover: party-suffix
   stripping, district-number splitting, city/state/zip splitting, phone-
   column coalescing/selection - a UI would need to expose these as
   composable value transformers, not just column pickers.
3. **`SourceEntry` party attribute**, for states that split sources by party
   rather than by row — a confirmed need for at least AL, MA (and probably a
   couple of the remaining "ready to build" states above). Both New Mexico
   and South Carolina turned out **not** to need this after all, despite
   looking identical in the research CSV (two rows, Democratic/Republican,
   same URL, for both): each portal's party control is just an optional
   filter on one unified export/search, not a mandatory per-party split like
   AL/MA's real separate files - the same "don't trust the CSV's row shape,
   verify the actual mechanism" lesson MS's UA-inversion false positive
   already taught, twice more now, just for source *shape* instead of
   bot-blocking.
4. **A shared "CSV-backed state" collector abstraction — investigated
   2026-09-03, not built.** Six data points (MD, NC, WY, HI, MS, NE) turned
   out to disconfirm one shared collector shape more than confirm it: at
   least four genuinely different election-discovery algorithms (HTML
   `<dt>/<dd>` scrape, JSON-LD tree walk, plain-text regex, two different
   "derive from the file itself" mechanisms) and two different row-attribution
   rules exist across just these six — and CO/VT/VA/NM/SC/SD/MT (seven more
   discovery algorithms: page-heading-year-crosscheck, evergreen-page-plus-
   templated-XLSX, two-hop-index-then-page discovery, NM's own hand-
   maintained-id-plus-separate-evergreen-date-page, SC's single-JSON-call
   election-list-with-dates-included, SD's hand-maintained-id-plus-
   still-live-evergreen-date-page, and MT's bare-URL-redirect-to-a-dropdown-
   that-already-has-everything - a fifth distinct variant, and the first
   fully self-discovering one on the exact same underlying platform NM/SD
   both needed hand-maintained ids for) only reinforced that conclusion. A full
   collector-level abstraction would either grow an
   escape-hatch interface with as many strategy slots as states (a config
   table in name only) or force real algorithmic differences to pretend to
   be interchangeable. What *was* real and low-risk got extracted instead:
   the field-mapping table above (item 2), and collapsing the identical
   `IPublishSchedule` implementations every state so far shares verbatim is
   flagged as the next easy win, not yet done. Election discovery,
   row→election attribution, and the office/district split cascade stay
   hand-written per state.

## Working conventions (unchanged throughout every phase)

- Every change verified against real live government data, not just unit
  tests, before being called done.
- A bug found via output inspection gets fixed and locked in with a named
  regression test, not just patched.
- A bare `curl` 403 is not sufficient evidence of blocked — confirm with a
  real browser UA at minimum, and the reverse (a bare/tool-like UA) when a
  browser UA alone still looks blocked (Mississippi). A `200` isn't
  automatically sufficient evidence of open either — check the response
  actually has real content, not a silently empty body (Arkansas, edge-cached
  for 10 minutes once triggered).
- Plan docs updated after each milestone with enough technical detail to
  stand alone.
- Never commit to git without being explicitly asked.
- New states follow the established architectural conventions — see
  `configDrivenPipelineInfo.md` section 1 — rather than inventing new
  per-state patterns where an existing one fits.
