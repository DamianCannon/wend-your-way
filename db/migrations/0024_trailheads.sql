-- 0024: Trailheads (PLAN T023, STORY-035): car parks the public may use beside a large Off-road
-- network, none within 1 km of another on it, found by the build after the Off-road networks.
create table network.trailheads (
  id          serial primary key,
  -- The car park in OSM: an area or a node.
  osm_kind    text not null check (osm_kind in ('area', 'point')),
  osm_id      bigint not null,
  name        text,
  -- An area's point on its surface.
  geom        geometry(Point, 27700) not null,
  network_id  integer not null,
  -- Its network's metres of green ground.
  network_m   real not null check (network_m > 0)
);
create index ix_trailheads_geom on network.trailheads using gist (geom);
