-- 0037: Trail Rides (PLAN T021; SPEC R9.6). The highway authorities' Definitive Map byways, from
-- rowmaps.com's open data, loaded by each build from data/rights-of-way; and the areas whose
-- Definitive Map isn't open, where OSM's byways are Unconfirmed. Kept outside network, so a build
-- reads them and a swap leaves them be.
create schema rights_of_way;

create table rights_of_way.byways (
  id        serial                    primary key,
  authority text                      not null,
  geom      geometry(LineString, 27700) not null
);
create index ix_byways_geom on rights_of_way.byways using gist (geom);

create table rights_of_way.without_data (
  id        serial                       primary key,
  authority text                         not null,
  geom      geometry(MultiPolygon, 27700) not null
);
create index ix_without_data_geom on rights_of_way.without_data using gist (geom);

-- Each Segment's Trail Ride access, and the months OSM closes it to a trail bike (bits, January
-- the lowest). Empty on a Network built before Trail Rides: a Trail Ride asks for
-- `ct network rebuild`, as a Gravel Ride did (0031).
alter table network.segments add column access_trail access_kind;
alter table network.segments add column closed_months smallint;
