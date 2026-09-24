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
| `roster_staging` | `dotnet run --project src/StateBallot.Cli -- --migrate` | Collection passes, per-election runs, and their rows (FluentMigrator, `src/StateBallot.Staging/Migrations/`) |

## Run

MySQL is a service in the repo root `docker-compose.yml`, alongside the
collector. Start it from the repo root, not from this directory.

```bash
docker compose up -d mysql
# wait until healthy, then create the staging schema:
dotnet run --project src/StateBallot.Cli -- --migrate
docker exec roster-vote-test mysql -uroot -proster vote -e \
  "SELECT ElectionKey, ElectionDesc FROM Elections; SELECT UserName, UserSecurity FROM Security;"
docker exec roster-vote-test mysql -uroot -proster roster_staging -e "SHOW TABLES; SELECT * FROM SchemaMigrations;"
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

The unit of record is one election. A CLI run collects a whole state and
year in one fetch, because that is how the sources publish, and that fetch is a
**collection pass**. The pass fans out into one **run** per election.

| Table | Grain | Holds |
| --- | --- | --- |
| `CollectionPasses` | one run | who ran it, args, git sha, log, gaps, source manifest, counts |
| `Runs` | one election | the election's own fields, pending flag, counts, resolution columns |
| `RunCandidates`, `RunMeasures` | one row per candidate or measure | the collector output, plus resolution columns |
| `RunCountyBallots` + `...Candidates` / `...Measures` | one county's ballot for that election | what that county's voters see, verbatim |
| `PassCountyDirectory` | state level | county elections offices, belongs to no election |
| `PassProposedMeasures` | state level | statewide measures with no ballot date yet |
| `PassUnassignedRows` | exception log | rows whose election could not be identified, with the reason |

Each row records the source system's own election id, so placing it on a run is
exact. Where an id is missing the matcher falls back to a unique election-type
match on the date, then to a single election on the date. Anything it still
cannot place lands in `PassUnassignedRows` rather than being dropped. The
fallback is not always enough on its own: TX ran two special elections on
2026-11-03, both typed Special.

```bash
# every election for a state and year
dotnet run --project src/StateBallot.Cli -- --state WV --year 2026

# one election only
dotnet run --project src/StateBallot.Cli -- --state WV --year 2026 --election 2026-11-03

# also export files (the data-repo path)
dotnet run --project src/StateBallot.Cli -- --state WV --output-root ../state-roster-data
```

Without an output root nothing is written to disk: the database is the store of
record. `--dry-run` collects and reports without storing.

## Migrations

[FluentMigrator](https://fluentmigrator.github.io/) classes under
`src/StateBallot.Staging/Migrations/`. `StateBallot.Staging.Migrator` wraps the
runner (run via `--migrate`, and by the console API at startup) and creates the
schema itself when missing. Applied versions are recorded in `roster_staging.VersionInfo`.

Adding one: a new class `M00n_<Name>` with `[Migration(n, "<description>")]`,
implementing `Up` and `Down`.

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
