-- 0044: what the Network was built from (PLAN T117): the extract's own date, as osm2pgsql recorded it
-- (its replication timestamp, else its newest change; none if neither), and when it was built. One
-- row, written by the build into network_next, so it swaps in with that Network and a failed build
-- leaves the old one's.
create table network.built_from (
  id           boolean primary key default true,
  osm_data_at  timestamptz,
  built_at     timestamptz not null default now(),
  constraint ck_built_from_one_row check (id)
);
