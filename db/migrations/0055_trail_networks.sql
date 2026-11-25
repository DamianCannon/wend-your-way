-- 0055: Trail Ride hotspots (T189, #193; STORY-039's AC3): the off-road ways a Trail Ride may use, grouped where
-- they lie within 2 km of one another (a motorbike may use every road, so joining through roads would join them
-- all). Those of 5 km or more are kept, so the line for showing one can move without a build. Each with its
-- metres, the box round it, a junction on it near its middle (by length) to mark it at, and the nearest town or
-- village in OS Open Names, if they were imported. As gravel_networks (0033) is.
create table network.trail_networks (
  id     serial                   primary key,
  metres real                     not null,
  at     geometry(Point, 27700)   not null,
  box    geometry(Polygon, 27700) not null,
  near   text,
  constraint ck_trail_networks_has_metres check (metres > 0)
);

-- A Trail Ride's car parks: each Trailhead's largest kept group with a way within 100 m of it, and its metres;
-- none if none is that near. Until a map is built with them, a Trail Ride's car parks are the Gravel networks'.
alter table network.trailheads add column trail_network_id integer;
alter table network.trailheads add column trail_network_m real;
