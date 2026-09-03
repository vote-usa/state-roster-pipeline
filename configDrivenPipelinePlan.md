# Config-Driven Pipeline: Plan

Clean rewrite of the working plan as of 2026-09-03. For what's actually built
and how it works, see [`configDrivenPipelineInfo.md`](configDrivenPipelineInfo.md)
— this doc is forward-looking: what's done at a glance, and what's left.
The messy, narrative version of this plan (every decision, every dead end,
kept for historical detail) remains at
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
| 3 — 50-state reality check + new-state onboarding | **In progress** | Research across all 50 states' real formats/fetch-blocking; format-parser infra (CSV, XLSX) built; 7 new states onboarded (MD, NC, WY, HI, MS, NE, CO). |

11 states implemented total: WA, CA, TX, WV (pre-existing) + MD, NC, WY, HI,
MS, NE, CO (added under Phase 3). Full technical detail on all 11 is in
`configDrivenPipelineInfo.md`.

## Phase 3 in detail

### What research changed about the plan

A 50-state research spreadsheet (source data, not persisted in this repo)
showed the 4-state sample (WA/CA/TX/WV) undersold real diversity:

- **Format**: CSV (~20 states) and XLSX (~14) are bigger buckets than the
  HTML/JSON/PDF formats already handled — and the cheapest to add, since
  `CsvHelper` was already a dependency (used for output) and only one new
  library (ClosedXML) was needed for XLSX.
- **Bot-blocking is the norm, not TX's edge case** — ~22 of 50 states flagged
  blocked. Only TX needed header-spoofing before this phase; that pattern
  needs to become a general, tiered, config-driven fetch strategy rather than
  one-off code, eventually covering plain GET → spoofed-header GET → session
  priming → full form/postback replay → headless-browser rendering.
- **Party is sometimes a source-level split**, not a per-row field (AL, MA,
  NM, SC each publish separate URLs per party) — `SourceEntry` doesn't yet
  have a way to say "every row from this source is party X."
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

### New-state onboarding — 7 done (MD, NC, WY, HI, MS, NE, CO), triage tracked below

Each state was researched live (direct `curl`/browser checks against the
real government site — the research spreadsheet's own flags turned out
wrong in both directions: several "blocked" states were wide open, and one
"open" DC endpoint actually needs session priming), designed, built following
the established file-per-concern convention (SourceConfig / Selectors /
Mapper / Collector / PublishSchedule), wired into the catalog/CLI/solution,
tested, and live-verified end-to-end before being called done. Full
per-state writeups are in `configDrivenPipelineInfo.md`.

### CSV-state triage — remaining tiers

- **Tier A/B (done)**: North Carolina, Wyoming (A — verified ready
  immediately); Hawaii, Mississippi (B — needed one extra discovery pass to
  find the real export mechanism). Mississippi was originally triaged into
  Tier C below on a bare `curl` 403, but turned out to be a UA-inversion case
  (see its write-up in `configDrivenPipelineInfo.md`), not a genuine block -
  worth remembering when triaging the rest of this list: a 403 from a plain
  `curl` isn't sufficient evidence of real blocking, only of *some* rule
  worth investigating with a browser-like UA (and, per this state, also the
  reverse - a tool-like UA) before writing it off.
- **Tier C — verified blocked, gated on fetch-strategy tiering rather than
  anything state-specific**: New York (403, explicit Cloudflare challenge),
  Oklahoma (403, AWS ELB block), Rhode Island (verified 2026-09-03: the
  entire `vote.sos.ri.gov` host, including the `vote.ri.gov` alias, returns
  `403`/`cf-mitigated: challenge` on every path and every header combination
  tried — a genuine Cloudflare *interactive* JS challenge, not TX's simpler
  header-sniffing rule; `HttpFetcher.AddDefaultHeader` doesn't help here, same
  class of block as NY. Colorado picked instead once RI was parked - see its
  write-up in `configDrivenPipelineInfo.md`), Wisconsin (same
  `cf-mitigated: challenge` pattern, found while looking for RI's
  replacement, not independently pursued further), Minnesota (same pattern
  but via Radware/`perfdrive.com` rather than Cloudflare - the entire
  `mn.gov` domain redirects every request to a bot-check validator page).
- **Tier D — flagged blocked/complex in research, not independently
  verified, could hide more MD/NC/WY/HI-style false positives**: Nevada, New
  Mexico (both parties), Oregon (likely POST/session, not a plain GET), South
  Carolina (both parties, needs per-district URL construction like
  Louisiana), South Dakota, Montana.
- **Tier 3 (XLSX, not bot-blocked, parser already built)**: Nebraska (done -
  see its write-up in `configDrivenPipelineInfo.md`; also the first state to
  need `XlsxTableParser`'s new `sheetIndex` parameter, for a second worksheet
  in the same workbook), Colorado (done, same write-up location - not
  actually in the original research spreadsheet's list, picked fresh after
  RI turned out blocked), Vermont, Virginia.
- **Tier 5 — genuinely hard / semi-manual, separate from config work
  entirely**: Georgia (session token + recaptcha), Louisiana (two-call
  dedup across 64 parishes), Ohio (no master list, sample-ballot-lookup
  only), Michigan (verified 2026-09-03: no statewide CSV/XLSX/JSON export
  exists at all — the SOS "candidate listings" page is just a directory of
  links out to individual county clerk sites; the actual "who's on my
  ballot" source, `mvic.sos.state.mi.us`, sits behind a real Cloudflare JS
  challenge (`cf_clearance` cookie gate on every path, including `/`) rather
  than TX's lighter header-spoofable block — confirmed via direct `curl`:
  no-UA and browser-UA requests both return 403 with a "Just a moment..."
  interstitial body. `HttpFetcher.AddDefaultHeader` (TX's fix) does not
  apply here; would need a headless-browser fetch tier or per-county PDF
  scraping instead, neither attempted), Arkansas (verified 2026-09-03: real
  data exists and is well-shaped — `candidates.arkansas.gov`, a WordPress
  site with a custom REST route `/wp-json/metl/v1/all` backing a DataTables
  UI, returns clean JSON rows (`FilerID`/`CanBallotName`/`Descript`/
  `PartyAffiliation`/`FilingDate`, 198 current candidates) - but unlike every
  other bot-blocked state so far, no request `curl` originates itself (any
  UA, any header combination, exact DataTables param replay, with or without
  cookies) gets real data back; Cloudflare returns a `200` with a silently
  empty body instead of an explicit block, then edge-caches that empty
  answer for 10 minutes, which is what made this look param-shape-related at
  first. A real headless Chromium (Playwright, driven against the live page)
  gets full data back immediately and consistently, no interactive
  challenge/CAPTCHA involved - this is TLS/HTTP-fingerprint bot scoring, not
  an unsolvable challenge like MI's. Practical blocker: this pipeline has no
  deployment target yet (runs today only as a local CLI); the dev machine's
  regular network connection passed Cloudflare's scoring, but the likely
  eventual host (some AWS instance, per 2026-09-03 conversation) would run
  from a datacenter IP range that Cloudflare typically scores worse
  regardless of browser fingerprint quality - so a headless-browser fetch
  tier's viability for AR (and any future state gated the same way) can't be
  confirmed until there's a real deployment target to test from. Decision
  deliberately deferred rather than building the tier speculatively;
  revisit once a deployment target exists. Full working request shape (all
  `columns[]`/`order[]`/`search[]` DataTables params plus `postID=2941`,
  `CanBallotName=`, `Descript=`) is captured in this session's history if
  picked back up.

## What's still not started

Carried forward from Phase 3's original architecture recommendations, none
begun yet:

1. **Fetch-strategy tiering as a first-class, config-driven axis.**
   `HttpFetcher.AddDefaultHeader` (TX, and MS's UA-inversion variant) and
   `WebFormsPostback` (HI, MS) are the first real pieces of this, but neither
   is exposed as per-source config yet — both are still hardcoded into their
   state's collector. The next tier up, headless-browser rendering, has a
   concrete first customer now (Arkansas - see its Tier 5 write-up above) but
   is gated on a real deployment target existing to test IP-reputation
   against, not on anything left to design.
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
   rather than by row.
4. **A shared "CSV-backed state" collector abstraction — investigated
   2026-09-03, not built.** Six data points (MD, NC, WY, HI, MS, NE) turned
   out to disconfirm one shared collector shape more than confirm it: at
   least four genuinely different election-discovery algorithms (HTML
   `<dt>/<dd>` scrape, JSON-LD tree walk, plain-text regex, two different
   "derive from the file itself" mechanisms) and two different row-attribution
   rules exist across just these six. A full collector-level abstraction
   would either grow an escape-hatch interface with as many strategy slots as
   states (a config table in name only) or force real algorithmic
   differences to pretend to be interchangeable. What *was* real and
   low-risk got extracted instead: the field-mapping table above (Tier 2),
   and collapsing the identical `IPublishSchedule` implementations six
   states share verbatim is flagged as the next easy win, not yet done.
   Election discovery, row→election attribution, and the office/district
   split cascade stay hand-written per state - see the full analysis this
   date recorded in conversation for the complete stage-by-stage breakdown.

## Working conventions (unchanged throughout every phase)

- Every change verified against real live government data, not just unit
  tests, before being called done.
- A bug found via output inspection gets fixed and locked in with a named
  regression test, not just patched.
- Plan docs updated after each milestone with enough technical detail to
  stand alone.
- Never commit to git without being explicitly asked.
- New states follow the established architectural conventions — see
  `configDrivenPipelineInfo.md` section 1 — rather than inventing new
  per-state patterns where an existing one fits.
