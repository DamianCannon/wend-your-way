-- 0032: the Obstacles on each Segment (PLAN T069; CONTEXT.md: Obstacle): gates, stiles and the
-- like from OpenStreetMap's barrier nodes, and fords. A node inside a Segment is all the
-- Segment's; one on a junction is half each Segment's that meets there, so a Loop passing through
-- pays for it once. Shares of one kind on one Segment are added up.
-- No foreign keys, as with segment_beside: find_obstacles() replaces every row at once.
create table network.segment_obstacles (
  segment_id integer not null,
  kind       text    not null,
  share      real    not null,
  constraint pk_segment_obstacles primary key (segment_id, kind),
  constraint ck_segment_obstacles_kind check (kind in ('gate', 'cycle_barrier', 'kissing_gate', 'turnstile',
                                                        'motorcycle_barrier', 'stile', 'cattle_grid', 'ford')),
  constraint ck_segment_obstacles_has_share check (share > 0)
);
