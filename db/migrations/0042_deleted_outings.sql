-- 0042: Outings the user deleted (PLAN T106; user: "Gone, never re-imported"). The Outing itself,
-- its track, coverage and gaps are gone; what's kept is only enough to know it again, so a later
-- Strava export or GPX import skips it: its Strava id, its track's fingerprint, when it started.
create table app.deleted_outings (
  id          serial primary key,
  strava_id   bigint,
  track_hash  text not null,
  started_at  timestamptz,
  deleted_at  timestamptz not null default now()
);
create index ix_deleted_outings_strava_id on app.deleted_outings (strava_id);
create index ix_deleted_outings_track_hash on app.deleted_outings (track_hash);
create index ix_deleted_outings_started_at on app.deleted_outings (started_at);
