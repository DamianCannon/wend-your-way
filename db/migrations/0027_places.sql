-- 0027: places to search for by name and postcode (PLAN T064), loaded by `ct places import` from
-- OS Open Names and the map's own named green areas. A schema of its own: the Network's builds
-- never touch it. Names and postcodes are searched apart, each by indexes of their own.
create extension if not exists pg_trgm;
create schema if not exists places;

create table places.names (
  id       serial primary key,
  -- "Box Hill"; "RH5 6BY", or a postcode district's "RH5".
  name     text not null,
  -- A PlaceKind, by name.
  kind     text not null,
  -- Its borough, district or county, or a postcode's place, to tell two of a name apart.
  context  text,
  -- As it's searched: PlaceSearch.Key, or a postcode's PlaceSearch.PostcodeKey.
  key      text not null,
  geom     geometry(Point, 27700) not null
);
create index ix_names_key on places.names (key text_pattern_ops) where kind not in ('Postcode', 'District');
create index ix_names_key_words on places.names using gin (key gin_trgm_ops) where kind not in ('Postcode', 'District');
create index ix_names_geom on places.names using gist (geom) where kind not in ('Postcode', 'District');
create index ix_names_postcode on places.names (key text_pattern_ops) where kind in ('Postcode', 'District');
