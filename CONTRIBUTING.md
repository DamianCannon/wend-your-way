# Contributing

Wend Your Way is a personal project, built by one developer with Claude Code. These notes exist so that work stays consistent, whoever (or whatever) is doing it.

## Get it running locally

Windows 11 with PowerShell. The setup script installs everything natively (no Docker). Run it once from an **administrator** PowerShell; it asks for a password for the PostgreSQL superuser and is safe to re-run:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\setup.ps1   # .NET 10 SDK, PostgreSQL 17 + PostGIS, osm2pgsql; creates the database
dotnet build
wend setup check          # (from Slice 1 onwards) confirms every piece is present and working
```

The app connects as the `cutthrough` login, whose password lives only in `%APPDATA%\postgresql\pgpass.conf`, so connection strings never contain a password.

Map data isn't committed. Download a Geofabrik extract (e.g. `greater-london-latest.osm.pbf`) into `data/`, then run `wend network build`. The Thunderforest API key lives in `dotnet user-secrets`, never in git.

## Run the tests

```powershell
dotnet test tests/WendYourWay.Core.Specs                  # fast: Core specs, no database (seconds)
dotnet test tests/WendYourWay.Web.Specs                   # the screens in bUnit, no database
dotnet test -m:1                                         # everything above and the integration specs, needs local PostgreSQL
```

`-m:1` runs the projects one after another: the Web specs that start the app and the integration
specs share `cutthrough_test`, which the integration specs wipe, so side by side they can fail
(found in T130: twelve of the served app's specs at once, passing on their own).

The default run leaves out two kinds of spec (`tests/default.runsettings`): those that read your
built Network (`RealNetwork`: measurements and guards on London's or Great Britain's map, minutes)
and the timings (`Performance`). Run them by name, after a rebuild or a change to the weights:

```powershell
dotnet test tests/WendYourWay.Integration.Specs -- NUnit.Where="cat == RealNetwork"
dotnet test tests/WendYourWay.Core.Specs -c Release -- NUnit.Where="cat == Performance"
```

Integration specs run against their own `cutthrough_test` database (created by `scripts/setup.ps1`), which they wipe at the start of every run; your real `cutthrough` database is never touched. They refuse to wipe any database whose name doesn't end in `_test`.

The rated-Loop guard (every Loop you called very good outscores every one you called good) reads your built Network, so it's a `RealNetwork` spec; the starred-routes guard reads your live ratings and runs only when asked by name (`[Explicit]`).

Routing specs draw their networks as ASCII maps (`tests/WendYourWay.Core.Specs/Support/AsciiNetwork.cs`): capital letters and digits are junctions, `.`/`:` Off-road, `-`/`|` Traffic-free, `=`/`#` roads, `x` forbidden; one character across is 50 m, one line down 100 m. The path finder is also checked against a deliberately naive reference search on hundreds of seeded random networks.

## What we care about

- **The glossary is law.** Use the terms in [CONTEXT.md](CONTEXT.md) exactly (Loop, Segment, Connector, Crossing, Flow, Exposure, Greenery, Outing, Mark…) in code, tests, UI and commits. If a new concept appears, add it to the glossary first.
- **Core stays pure.** `WendYourWay.Core` has no I/O, no clock and no database. Scoring, routing and generation are tested in milliseconds.
- **One cost function.** Routing and scoring share `ScoringWeights`. Weights live in code, not the database, and every change must keep the Reference Loop specs green.
- **Test-first, one test at a time.** Tests are NUnit, named Feature / Scenario / Specification after the stories in GitHub Issues. Write one test, make it pass, then the next; don't bulk-generate failing specs.
- **Database rules.** `snake_case`, `id serial primary key`, `timestamptz`, enums over lookup tables, set-based logic in plpgsql. User data never holds a foreign key into the rebuildable `network` schema ([ADR 0005](docs/adr/0005-user-data-keys-segments-by-osm-identity.md)).
- **Decisions are written down.** Hard-to-reverse, surprising choices get an ADR in `docs/adr/`; smaller ones are noted on the issue they came up in.

## Proposing a change

- **Small and obvious** (a typo, a clear bug): commit straight to `main`.
- **Anything bigger**: open an issue first, so the design question is settled before code, then work on a short-lived branch and open a pull request using the template.
- Work items are [GitHub Issues](https://github.com/DamianCannon/wend-your-way/issues), one per task, titled with its id (`T138 · …`), labelled `task` and its stories, in a milestone for its part of the plan. One task is one reviewable, committable unit; its commit says `Closes #N`.

## Commit messages

[Conventional Commits](https://www.conventionalcommits.org/): `<type>(<scope>): <imperative summary>`, for example:

```
feat(core): penalise reused same-ground groups when finding later Paths
fix(data): clip segments to England and Wales before grouping
docs(adr): record the in-process router decision
```

Types: `feat`, `fix`, `docs`, `test`, `refactor`, `perf`, `build`, `chore`. Scopes are usually a project (`core`, `data`, `web`, `cli`) or a docs area (`adr`, `spec`, `plan`).
