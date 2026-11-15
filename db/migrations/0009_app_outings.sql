-- 0009: the user's own runs, apart from the Network (PLAN T015, STORY-030, ADR 0005).
--
-- The app schema holds what the user made or recorded. The Network is rebuilt wholesale and
-- renumbers its Segments, so nothing here has a foreign key into network: Segments are named
-- by their OSM identity. A Network build never touches this schema.

-- What a Loop or an Outing was for. In public, like the other enums, so it survives the
-- network_next → network swap; the Network's per-Activity columns will use it too.
create type activity as enum ('run', 'gravel_ride');

create schema app;

-- A run or ride the user recorded (CONTEXT.md: Outing). Slice 2 imports GPX files; the Strava
-- bulk export (increment 4) will fill strava_id and the rest of ARCHITECTURE's columns.
create table app.outings (
  id           serial primary key,
  activity     activity not null,
  name         text not null,
  -- The file it came from, and a hash of its track: importing the same track twice is a no-op.
  source_file  text not null,
  track_hash   text not null,
  -- From the GPX's timestamps, when it has them.
  started_at     timestamptz,
  moving_time_s  int check (moving_time_s > 0),
  geom         geometry(LineString, 27700) not null,
  length_m     real generated always as (ST_Length(geom)) stored,
  -- Share of the track near a Segment it covered; null until coverage is computed.
  coverage     real check (coverage between 0 and 1),
  -- nullable: only Outings from the Strava export have one.
  strava_id    bigint,
  constraint ux_outings_track_hash unique (track_hash),
  constraint ux_outings_strava_id unique (strava_id)
);

-- The Segments an Outing covered, by OSM identity (ADR 0005). Derived from the track, and
-- recomputed after every Network build, since an identity changes when OSM adds a junction
-- (Slice 1 review).
create table app.outing_coverage (
  outing_id     int    not null references app.outings (id) on delete cascade,
  osm_way_id    bigint not null,
  from_node_id  bigint not null,
  to_node_id    bigint not null,
  constraint pk_outing_coverage primary key (outing_id, osm_way_id, from_node_id, to_node_id)
);
