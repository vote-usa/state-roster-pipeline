# Partial VoteUSA test database

Local MySQL 8.4 with the VoteProject.5 tables relevant to the ballot roster, so it does not include all tables. Based on the contents of `VoteLibrary/DB/Vote.designer.cs` (`InitAllColumns`).

This is a staging db for mapping pipeline output onto VoteUSA keys. It is not a clone of production VoteUSA.

## Design

For the civic roster tables, map pipeline entities onto:

| Pipeline | VoteUSA table | Identity |
| --- | --- | --- |
| election | `Elections` | `ElectionKey` = `{ST}{yyyyMMdd}{type}{party}` (12 chars statewide). Types: `G` general, `P` primary, `S` special, `Q` primary runoff, `R` general runoff. General elections use national party `A` (all). |
| office | `Offices` + `ElectionsOffices` | `OfficeKey` via VoteUSA `Offices.CreateOfficeKey` (e.g. `CAGovernor`, `CAUSHouse14`). `OfficeLevel` is the `OfficeClass` int. |
| candidate | `Politicians` + `ElectionsPoliticians` | `PoliticianKey` = `{ST}{Last}{First}{Middle}{Suffix}` (`Politicians.GetUniqueKey`), then digit suffix on collision. |
| measure | `Referendums` | `ReferendumKey` (up to 150 chars). |
| county directory | `Counties` | `CountyCode` is the **3-digit** county FIPS (pipeline `county_fips.json` is 5-digit; use the last three). |

Two schemas live in the one local MySQL:

| Schema | Created by | Contents |
| --- | --- | --- |
| `vote` | `schema.sql` + `smoke.sql` at container init | VoteUSA-shaped roster tables, `Security` (console sign-in), `LocalDistricts`, `RosterProvenance` |
| `roster_staging` | `dotnet run --project src/StateBallot.Cli -- --migrate` | Source links, captures and their fetch logs, collection passes, per-election runs, and their rows (FluentMigrator, `src/StateBallot.Staging/Migrations/`) |

## Run

MySQL is a service in the repo root `docker-compose.yml`, alongside the
collector. Start it from the repo root, not from this directory.

```bash
docker compose up -d mysql
# wait until healthy, then create the staging schema:
dotnet run --project src/StateBallot.Cli -- --migrate
docker exec roster-vote-test mysql -uroot -proster vote -e \
  "SELECT ElectionKey, ElectionDesc FROM Elections; SELECT UserName, UserSecurity FROM Security;"
docker exec roster-vote-test mysql -uroot -proster roster_staging -e "SHOW TABLES; SELECT * FROM VersionInfo;"
```

The collector can also run in its container, reaching the database by service
name instead of the published port:

```bash
docker compose run --rm collector --migrate
docker compose run --rm collector --state WV
docker compose run --rm --no-deps collector --state TX --dry-run   # skips starting MySQL
```

Smoke sign-in users: `master` / `master` (MASTER) and `wvadmin` / `wvadmin`
(ADMIN scoped to WV).

## Staging shape

A run has two stages. The **capture** fetches every source page for a state and year and saves each one as fetched, because that is how the sources publish. A **collection pass** then normalizes that saved capture, never the network, and fans out into one **run** per election. The run is the unit of record. One capture can be normalized many times, for example after a parser fix, and each time is a new pass.

| Table | Grain | Holds |
| --- | --- | --- |
| `SourceLinks` | one URL per state, key and variant | the URL template a collector fetches (or its home page), format, notes, active flag, who last edited it. See "Source links" below |
| `SourceParameters` | one value per state, key and variant | hand-maintained values beside the links, e.g. NM/SD election ids and HI's bootstrap election id |
| `Captures` | one fetch of a state and year | who ran it, args, git sha, Wayback stamp, status, raw directory, fetch count, bytes, fetch log hash, log, and a snapshot of the source links it used (`LinksJson`) |
| `CaptureFetches` | one HTTP request | role and keys the collector tagged it with (e.g. `county-guide` {election, county}), method, URL, request body hash, status, content type, size, payload hash, file name, duration, error |
| `CollectionPasses` | one normalization of a capture | `CaptureId`, who ran it, args, git sha, log, gaps, source manifest with payload hashes, counts |
| `Runs` | one election | the election's own fields, pending flag, counts, resolution columns |
| `RunCandidates`, `RunMeasures` | one row per candidate or measure | the collector output, plus resolution columns |
| `RunCountyBallots` + `...Candidates` / `...Measures` | one county's ballot for that election | what that county's voters see, verbatim |
| `PassCountyDirectory` | state level | county elections offices, belongs to no election |
| `PassProposedMeasures` | state level | statewide measures with no ballot date yet |
| `PassUnassignedRows` | exception log | rows whose election could not be identified, with the reason |

The payloads themselves are not in the database. They are files under `data/raw/<xx>/<capture id>/` beside a `fetch_log.json` that mirrors `CaptureFetches`, gitignored and pruned to the newest few per state by `--keep-raw` (default 3). `CaptureFetches` rows are kept after the files are pruned, so a stored row can always be tied to the hash of the payload it came from. Passes stored before captures existed have a null `CaptureId`.

Each row records the source system's own election id, so placing it on a run is
exact. Where an id is missing the matcher falls back to a unique election-type
match on the date, then to a single election on the date. Anything it still
cannot place lands in `PassUnassignedRows` rather than being dropped. The
fallback is not always enough on its own: TX ran two special elections on
2026-11-03, both typed Special.

`County` on candidate and measure rows is null for statewide, federal, legislative and judicial rows, one name for a local row, and a `"; "`-joined list for a race or measure that spans counties. Those columns are TEXT so a long list can never fail a pass. `RunCountyBallots.County` is always one county name.

`RunCandidates.Status` is the candidate's filing status exactly as the source words it ("Active", "Withdrawn - 02/19/2026", "Seeking the Nomination"), null when the source publishes none. It is not the run's own `Runs.Status`, and it is not normalized: a withdrawn candidate is still a stored row, so a consumer has to read this column to leave them off a ballot.

```bash
# every election for a state and year: capture, then normalize into a pass
dotnet run --project src/StateBallot.Cli -- --state WV --year 2026

# one election only
dotnet run --project src/StateBallot.Cli -- --state WV --year 2026 --election 2026-11-03

# capture only, then normalize it later (offline, a new pass each time)
dotnet run --project src/StateBallot.Cli -- --state WV --capture-only
dotnet run --project src/StateBallot.Cli -- --state WV --normalize 1

# also export files (the data-repo path)
dotnet run --project src/StateBallot.Cli -- --state WV --output-root ../state-roster-data
```

Without an output root no roster files are written, only the raw capture: the database is the store of record. `--dry-run` captures under `data/raw/<xx>/dry-<utc stamp>/` and normalizes it, but stores nothing in the database.

## Source links

Every URL a collector fetches lives in `SourceLinks`, so the console can view and edit it,
along with each state's `home` page (named in the error when a run comes back empty). Hand-maintained
values such as election ids live in `SourceParameters`. A link's `LinkKey` is the fetch role
the collector tags that download with, so it joins to the `CaptureFetches` rows it produced.
`Variant` splits a key by election type (`primary` / `general`). `UrlTemplate` may hold
`{year}`, `{electionId}`, `{countyCode}` or `{raceId}`. An unknown placeholder fails on load,
and a missing value fails when the URL is built.

The seed is one file per state in git, `data/input/<xx>/source_links.json`. It survives
every database wipe.

- `--migrate` inserts any seed row the database is missing and **never overwrites an
  existing row**, so console edits survive. Rows that differ from the seed, or that only
  the database has, are listed in its output.
- `--export-links [XX]` writes the database rows back to the seed files. Run it before
  wiping a database whose edits you want to keep, then commit the files.
- `--reseed-links [XX]` replaces the database rows with the seed files. Rows only in the
  database are deleted.

The dev loop: `docker compose down -v && docker compose up -d mysql`, `--migrate` (every
link is seeded), edit in the console or SQL, `--export-links`, commit.

Which links a run uses:

| Run | Links come from |
| --- | --- |
| Stored capture | `SourceLinks`. Fails with "run --migrate" when the state has no rows. |
| `--dry-run` capture | `SourceLinks` when the database is reachable and has the state's rows, otherwise the seed file. The run log says which. |
| Normalize (including `--normalize <id>`) | The capture's own snapshot (`fetch_log.json` `links`, mirrored in `Captures.LinksJson`), so a later edit never changes what an old capture's rows say they came from. Captures made before migration 5 have no snapshot and use the current links. |

## Migrations

[FluentMigrator](https://fluentmigrator.github.io/) classes under
`src/StateBallot.Staging/Migrations/`. `StateBallot.Staging.Migrator` wraps the
runner (run via `--migrate`, and by the console API at startup) and creates the
schema itself when missing. Applied versions are recorded in `roster_staging.VersionInfo`.

Adding one: a new class `M00n_<Name>` with `[Migration(n, "<description>")]`, implementing `Up` and `Down`. Use `AsAnsiString`, never `AsString` (NVARCHAR becomes utf8mb3), and `AsCustom` for TEXT, MEDIUMTEXT and JSON. Never edit a migration once anyone else's database has applied it. Update this README in the same change.

| Version | Class | Change |
| --- | --- | --- |
| 1 | `M001_StagingRuns` | collection passes, per-election runs and their rows |
| 2 | `M002_RawCapture` | `Captures`, `CaptureFetches`, `CollectionPasses.CaptureId` |
| 3 | `M003_CountyListText` | `County` on `RunCandidates`, `RunMeasures`, `PassProposedMeasures` becomes TEXT |
| 4 | `M004_CandidateStatus` | `RunCandidates.Status`, the candidate's filing status as the source publishes it |
| 5 | `M005_SourceLinks` | `SourceLinks`, `SourceParameters`, `Captures.LinksJson` |

## Tests

`dotnet test` runs the fast suite only (no Docker, no network). The live
suite (`StateBallot.Staging.Tests/LiveCollectionIntegrationTests.cs`) runs
every implemented state's collector against real sources and a throwaway
Testcontainers MySQL - useful for catching schema/data-shape mismatches
(column too narrow, a collector's constructor contract broken) that only
show up against real data, but slow (2-3 minutes) and needs Docker. Run it
explicitly:

```bash
dotnet test --filter Category=Integration
```

Connection strings come from `ROSTER_STAGING_CONNECTION` and `VOTE_CONNECTION`
(defaults point at this Docker setup).

Host port **3307** (avoids colliding with a local 3306). User/password/database:
`roster` / `roster` / `vote`. Root password: `roster` (local test only).

Regen DDL after a VoteProject designer change:

```bash
dotnet run --project src/StateBallot.Tools
```

Re-init from scratch (destroys the volume, so `--migrate` again afterwards):

```bash
docker compose down -v && docker compose up -d mysql
dotnet run --project src/StateBallot.Cli -- --migrate
```
