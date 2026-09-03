# VoteUSA-shaped test database

Local MySQL 8.4 with the **roster-relevant** VoteProject.5 tables, reconstructed
from `VoteLibrary/DB/Vote.designer.cs` (`InitAllColumns`). VoteProject has no
checked-in `CREATE TABLE` dump.

This is a test harness for mapping pipeline output onto VoteUSA keys, then
exporting the existing JSON/CSV into `state-roster-data`. It is **not** a clone
of production VoteUSA and must not be pointed at the RDS instances in
VoteProject `Web.config`.

## Reuse verdict

**Yes, for the civic roster tables.** Map pipeline entities onto:

| Pipeline | VoteUSA table | Identity |
| --- | --- | --- |
| election | `Elections` | `ElectionKey` = `{ST}{yyyyMMdd}{type}{party}` (12 chars statewide). Types: `G` general, `P` primary, `S` special, `Q` primary runoff, `R` general runoff. General elections use national party `A` (all). |
| office | `Offices` + `ElectionsOffices` | `OfficeKey` via VoteUSA `Offices.CreateOfficeKey` (e.g. `CAGovernor`, `CAUSHouse14`). `OfficeLevel` is the `OfficeClass` int. |
| candidate | `Politicians` + `ElectionsPoliticians` | `PoliticianKey` = `{ST}{Last}{First}{Middle}{Suffix}` (`Politicians.GetUniqueKey`), then digit suffix on collision. |
| measure | `Referendums` | `ReferendumKey` (up to 150 chars). |
| county directory | `Counties` | `CountyCode` is the **3-digit** county FIPS (pipeline `county_fips.json` is 5-digit; use the last three). |

**No, for the full VoteUSA schema.** Skip politicians’ bios/images/passwords,
answers, zip streets, cache, ads-as-product, etc. Those are augmentation or
app infrastructure. `Politicians` still has those columns (empty defaults) so
a later load into VoteUSA is column-compatible.

**Do not add FKs yet.** `PartyKey` is a VoteUSA 5-char code (`CAD`), not a
scraped party string. Seeding `Parties` from scrapes is a separate mapping.

Pipeline fields VoteUSA does not store (`source_url`, `ocd_division_id`,
`source_candidate_id`) go in `RosterProvenance` so the data-repo JSON/CSV can
be regenerated without inventing values.

Two schemas live in the one local MySQL:

| Schema | Created by | Contents |
| --- | --- | --- |
| `vote` | `schema.sql` + `smoke.sql` at container init | VoteUSA-shaped roster tables, `Security` (console sign-in), `LocalDistricts`, `RosterProvenance` |
| `roster_staging` | `dotnet run --project src/StateBallot.Cli -- --migrate` | Pipeline runs, resolution, change sets, merges (`migrations/*.sql`) |

`Security` and `LocalDistricts` are included because the console authenticates
against VoteProject's user table and office resolution needs local-jurisdiction
keys. `CacheInvalidation` lives in VoteProject's separate cache database and is
not reconstructed here.

## Run

```bash
cd db
docker compose up -d
# wait until healthy, then apply the staging migrations:
cd .. && dotnet run --project src/StateBallot.Cli -- --migrate
docker exec roster-vote-test mysql -uroot -proster vote -e \
  "SELECT ElectionKey, ElectionDesc FROM Elections; SELECT UserName, UserSecurity FROM Security;"
docker exec roster-vote-test mysql -uroot -proster roster_staging -e "SHOW TABLES; SELECT * FROM SchemaMigrations;"
```

Smoke sign-in users: `master` / `master` (MASTER) and `wvadmin` / `wvadmin`
(ADMIN scoped to WV). Local test only.

## Migrations

Plain SQL under `migrations/`, applied in filename order by
`StateBallot.Staging.Migrator` (run via `--migrate`, and by the console API at
startup). Each applied file is recorded in `roster_staging.SchemaMigrations`
with a checksum. Applied files are immutable: to change the schema, add a new
numbered file. The migrator creates the `roster_staging` schema if missing;
`00-grants.sql` gives the `roster` user rights on it at container init.

Connection strings come from `ROSTER_STAGING_CONNECTION` and `VOTE_CONNECTION`
(defaults point at this Docker setup); `ROSTER_MIGRATIONS_DIR` overrides the
migrations path.

Host port **3307** (avoids colliding with a local 3306). User/password/database:
`roster` / `roster` / `vote`. Root password: `roster` (local test only).

Regen DDL after a VoteProject designer change:

```bash
dotnet run --project src/StateBallot.Tools
```

Re-init from scratch (destroys the volume):

```bash
cd db && docker compose down -v && docker compose up -d
```

## Not done yet

Collectors still write JSON/CSV via `ResultWriter`. Wiring `CollectResult` →
this DB → export is the next slice. Office classification (free-text `office`
→ `OfficeClass` / `OfficeKey`) is the hard mapping; do not invent fuzzy
matchers — that is the `offices.yml` problem from the mini-spec.
