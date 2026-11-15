-- 0036: Marks (PLAN T082, SPEC R9.2): what the user says of a Segment, kept by its OSM identity
-- (ADR 0005) so a Mark outlives Network rebuilds; one whose Segment is gone has no effect. Blocked
-- first: a Segment no Loop may use. Grim and Gem, a Mark for one Activity, and months come later.
create table app.marks (
  id           serial      primary key,
  osm_way_id   bigint      not null,
  from_node_id bigint      not null,
  to_node_id   bigint      not null,
  kind         text        not null,
  marked_at    timestamptz not null default now(),
  constraint ck_marks_kind check (kind in ('blocked')),
  constraint ux_marks_segment unique (osm_way_id, from_node_id, to_node_id)
);
