-- 0034: car parks for a Gravel Ride (PLAN T073). The Segments of each Gravel Off-road network kept
-- (25 km or more), and each Trailhead's Gravel network: the largest kept with a Segment within
-- 100 m of it, and its metres; none if no such network is that near.
-- No foreign keys, as with segment_offroad: the build replaces every row at once.
create table network.segment_gravel (
  segment_id integer not null,
  network_id integer not null,
  constraint pk_segment_gravel primary key (segment_id)
);
create index ix_segment_gravel_network_id on network.segment_gravel (network_id);

alter table network.trailheads add column gravel_network_id integer;
alter table network.trailheads add column gravel_network_m real;
