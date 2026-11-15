-- 0026: what a Trailhead is called and the roads a car reaches it by, found by the build (PLAN T025,
-- user, 2026-10-29: "compute all of these car parks up front"), not on every Find a start. The
-- drive from home is still worked out in memory only, so home's position is never stored.
alter table network.trailheads add column label text not null default 'Car park';
-- Both ends of the few roads a car may use nearest it: the very nearest may be cut off.
alter table network.trailheads add column drive_nodes bigint[] not null default '{}';
