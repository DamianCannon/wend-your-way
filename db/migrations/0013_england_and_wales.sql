-- 0013: the Network covers England and Wales only (PLAN T030, STORY-002 AC1, R8.3).
--
-- build_segments() fills this from OSM's country relations (osm.countries, written by the flex
-- style), cut into small pieces so a point or line is tested against a few hundred vertices,
-- not a whole coastline. It stays empty when the extract doesn't hold both countries whole (a
-- regional extract such as London's): then nothing is clipped, no Request is refused and no
-- Outing is skipped, rather than losing whichever country is missing.
create table network.england_and_wales (
  id    serial primary key,
  geom  geometry(Polygon, 27700) not null
);
create index ix_england_and_wales_geom on network.england_and_wales using gist (geom);

-- What the clip did, so nothing is dropped silently.
alter type segment_build
  add attribute countries_found  text,     -- the countries the extract held whole, e.g. 'GB-ENG,GB-SCT,GB-WLS'
  add attribute in_scotland      bigint,   -- skipped: in Scotland (null when not clipped)
  add attribute elsewhere        bigint;   -- skipped: in none of the three (the sea, abroad)
