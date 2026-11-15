-- 0001: the Network's Segments: only what Slice 1 needs (PLAN T003).
-- Later migrations add Hazard, Greenery, Obscurity, same-ground groups and the rest
-- (ARCHITECTURE.md, "network schema").

-- PostGIS itself is enabled by scripts/setup.ps1 as the superuser. This only
-- documents the dependency: it's a no-op when the extension already exists.
create extension if not exists postgis;

-- Enums live in public, not in network: the Network is rebuilt into network_next and
-- swapped in by renaming schemas, and types must survive that swap.
create type ground_kind as enum ('off_road', 'traffic_free', 'connector');
create type access_kind as enum ('allowed', 'unconfirmed', 'push_only', 'forbidden');

create schema network;

-- The stretch of a single way between two junctions (CONTEXT.md: Segment).
-- ~20–30M rows for England and Wales; serial (2.1B) is ample.
create table network.segments (
  id            serial primary key,
  osm_way_id    bigint      not null,
  from_node_id  bigint      not null,
  to_node_id    bigint      not null,
  geom          geometry(LineString, 27700) not null,
  -- double precision, not real: the path finder refuses a Segment shorter than the
  -- straight line between its junctions, and float4 rounding could trip that.
  length_m      double precision generated always as (ST_Length(geom)) stored,
  ground        ground_kind not null,
  access_run    access_kind not null,
  constraint ck_segments_has_length check (ST_Length(geom) > 0)
);

-- A Segment's OSM identity survives Network rebuilds; user data refers to it (ADR 0005).
create unique index ux_segments_osm_identity on network.segments (osm_way_id, from_node_id, to_node_id);
create index ix_segments_geom on network.segments using gist (geom);
create index ix_segments_from_node_id on network.segments (from_node_id);
create index ix_segments_to_node_id on network.segments (to_node_id);
