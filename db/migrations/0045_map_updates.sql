-- 0045: the map's updates (PLAN T127): each run of `ct update`, the stage it has reached, a heartbeat
-- while it runs, and how it ended with a sentence why; the app shows the last. A row still running
-- with no heartbeat for a few minutes was stopped. In app, so backed up; a restored row still
-- running is shown as stopped by the same rule.
create table app.map_updates (
  id            serial primary key,
  started_at    timestamptz not null default now(),
  heartbeat_at  timestamptz not null default now(),
  finished_at   timestamptz,
  stage         text not null,
  outcome       text not null default 'running',
  message       text,
  source        text not null,
  -- When the source said its data runs to, once asked.
  source_at     timestamptz,
  constraint ck_map_updates_outcome check (outcome in ('running', 'finished', 'up to date', 'failed'))
);

-- Whether the last load of an extract into osm finished (T127): 'importing' while osm2pgsql runs,
-- 'imported' after. A rebuild from osm refuses after an import that didn't finish. One row.
create table osm.import (
  id      boolean primary key default true,
  state   text not null,
  at      timestamptz not null default now(),
  constraint ck_import_one_row check (id),
  constraint ck_import_state check (state in ('importing', 'imported'))
);
