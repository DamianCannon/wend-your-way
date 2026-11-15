-- 0010: where an Outing left the Network (PLAN T016, STORY-030 AC3).
--
-- A gap is a stretch of an Outing's track more than 25 m from any Segment: a path OSM may be
-- missing, or GPS gone astray. Derived from the track like app.outing_coverage, and worked out
-- again after every Network build.
create table app.outing_gaps (
  id         serial primary key,
  outing_id  int not null references app.outings (id) on delete cascade,
  geom       geometry(LineString, 27700) not null,
  length_m   real not null check (length_m > 0)
);

create index ix_outing_gaps_outing_id on app.outing_gaps (outing_id);
