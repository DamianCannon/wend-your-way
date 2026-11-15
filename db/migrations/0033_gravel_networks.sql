-- 0033: Gravel Off-road networks (PLAN T072; STORY-039): Off-road ground a bike may use, joined
-- through short roads as a Run's Off-road networks are. Those of 25 km or more are kept, so the
-- line for a Gravel hotspot (50 km) can move without a build. Each with its metres, the box round
-- it, a junction on it near its middle (by length) to mark it at, and the nearest town or village
-- in OS Open Names, if they were imported.
create table network.gravel_networks (
  id     serial                   primary key,
  metres real                     not null,
  at     geometry(Point, 27700)   not null,
  box    geometry(Polygon, 27700) not null,
  near   text,
  constraint ck_gravel_networks_has_metres check (metres > 0)
);
