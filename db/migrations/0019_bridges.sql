-- 0019: Bridges over water (PLAN T046): a Segment of a way tagged as a bridge that crosses water,
-- where a Loop along a river can cross back. Set by build_segments().
alter table network.segments add column bridge_over_water boolean not null default false;
