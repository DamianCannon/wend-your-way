-- 0023: Off-road networks (PLAN T022, STORY-035): green ground joined at its junctions and across
-- single short roads, as OffRoadNetworks finds them in a slice, found for the whole Network by the
-- build. In tables of their own, written by COPY: updating millions of Segments takes too long.

create table network.offroad_networks (
  id        integer primary key,
  -- Metres of green ground in it, and how many green Segments.
  metres    real not null check (metres > 0),
  segments  integer not null check (segments > 0),
  -- The box round its Segments' ends.
  extent    geometry(Polygon, 27700) not null
);
create index ix_offroad_networks_metres on network.offroad_networks (metres desc);
create index ix_offroad_networks_extent on network.offroad_networks using gist (extent);

-- Each green Segment's network, by network.segments.id.
create table network.segment_offroad (
  segment_id  integer primary key,
  network_id  integer not null
);
create index ix_segment_offroad_network_id on network.segment_offroad (network_id);
