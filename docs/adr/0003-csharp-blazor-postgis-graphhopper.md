# C# / Blazor Server / PostgreSQL + PostGIS / local GraphHopper

> The GraphHopper part of this decision is superseded by [ADR 0006](0006-route-in-process-over-network-slices.md). C#, Blazor Server and PostgreSQL + PostGIS stand.

Wend Your Way is built in C# on .NET 10, with a Blazor Server UI bound to localhost, PostgreSQL + PostGIS as the store and preprocessing engine, and GraphHopper running locally as a separate Java process for point-to-point routing and GPS matching. C# is the user's home language (34 .NET solutions in their projects folder) and has mature geometry support (NetTopologySuite, Npgsql's PostGIS mapping). PostGIS is the standard target for OSM preprocessing and is what Trail Router uses for its per-way scores. GraphHopper offers per-request custom models, polygon areas (used for Marks and Avoids) and built-in map-matching, and it handles a country-sized graph on a 16 GB laptop.

## Considered options

- **TypeScript on Bun + SQLite/SpatiaLite.** It fits the user's existing `/build-loop` and `/spec` tooling unchanged, but it's a less familiar stack with weaker geospatial libraries. Rejected; tests are written with the language-agnostic `tdd` skill instead.
- **BRouter.** Loved for off-road cycling, but its profile language makes Access rules and per-request user data awkward, and it has no map-matching.
- **pgRouting.** No extra process, but too slow to route hundreds of candidate legs in 30 seconds or a Nudge in 2–3 seconds at national scale.
- **Itinero** (a native C# router). It avoids Java, but has seen little recent development.

## Consequences

- Two non-.NET runtimes run on the laptop (PostgreSQL, Java). `scripts/setup.ps1` installs them natively via winget; there's no Docker, because its VM would cost several GB of RAM.
- The user's TypeScript-only build tooling doesn't apply. Tests are written test-first with the language-agnostic `tdd` skill instead of bdd-specs, and the `/build-loop` builder and reviewer agents would need C# variants before they could drive this project.
