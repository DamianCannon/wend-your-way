-- 0046: every job started from the app (PLAN T129), not only the map's updates: app.map_updates keeps its
-- name (backups taken before this restore into it as they are) and gains what kind of job each is. It's
-- the record of what was run, not the user's data, so backups leave its rows out and a restore leaves it
-- as it is.
alter table app.map_updates add column kind text not null default 'update'
  constraint ck_map_updates_kind check (kind in ('update', 'import', 'back', 'backup', 'restore'));

-- When each Outing came in (T129), for "last imported"; none for those before this was kept.
alter table app.outings add column imported_at timestamptz default now();
update app.outings set imported_at = null;
