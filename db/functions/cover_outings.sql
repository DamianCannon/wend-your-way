-- Which Segments an Outing covered, where it left the Network, and how much of it ran along what
-- it covered (PLAN T016, STORY-030; ARCHITECTURE "Coverage").
-- Repeatable: Migrations applies every script in db/functions after the numbered migrations, on
-- every run, so a function is changed by editing it here (create or replace). Schema changes
-- still go in numbered migrations.

-- Works out coverage for the given Outings (p_outings), or for all of them (null), replacing what
-- was there: on import, and after every Network build, since Segment identities can change then.
-- Call it at most once per transaction (its working tables last until commit).
drop function if exists cover_outings(integer);   -- it took one Outing until the Strava archive
create or replace function cover_outings(p_outings integer[] default null)
returns void
language plpgsql
volatile
security invoker
set search_path = public, pg_temp
as $$
declare
  -- A Segment is covered when most of it (c_most) lies within c_within_m of the track, running
  -- within 30° of the same way (|sin| < c_max_sin, either direction). GPS wanders more than OSM
  -- does, so this is wider than same ground's 12 m.
  c_within_m  constant double precision := 20;
  c_sample_m  constant double precision := 10;
  c_max_sin   constant double precision := 0.5;
  c_most      constant double precision := 0.6;
  -- A stretch of track more than c_gap_m from every Segment, at least c_least_gap_m long, is a
  -- gap. The track is sampled every c_track_sample_m to find them.
  c_gap_m           constant double precision := 25;
  c_least_gap_m     constant double precision := 10;
  c_track_sample_m  constant double precision := 5;
  -- Simplifying a track by 1 m changes no match at these distances.
  c_simplify_m      constant double precision := 1;
begin
  delete from app.outing_coverage where p_outings is null or outing_id = any (p_outings);
  delete from app.outing_gaps where p_outings is null or outing_id = any (p_outings);

  -- Each Outing's track, simplified to c_simplify_m (a recording has a point a second, far more
  -- than matching needs), in its straight pieces with their bearings.
  create temporary table tmp_track_pieces on commit drop as
  select o.id as outing_id, d.geom, ST_Azimuth(ST_StartPoint(d.geom), ST_EndPoint(d.geom)) as azimuth
    from app.outings o
   cross join lateral ST_DumpSegments(ST_Simplify(o.geom, c_simplify_m)) d
   where (p_outings is null or o.id = any (p_outings))
     and ST_Length(d.geom) > 0;
  create index on tmp_track_pieces using gist (geom);
  analyze tmp_track_pieces;

  -- Segments near enough to an Outing to have been covered by it, found piece by piece: against
  -- a whole track the spatial index barely helps (its box can span a borough), and this step took
  -- 2.8 s of 3.2 s on an 18 km Outing.
  create temporary table tmp_near on commit drop as
  select distinct t.outing_id, s.id, s.osm_way_id, s.from_node_id, s.to_node_id, s.geom
    from tmp_track_pieces t
    join network.segments s on ST_DWithin(s.geom, t.geom, c_within_m);

  -- Covered: most of the Segment, by sampled length, runs beside the track the same way.
  insert into app.outing_coverage (outing_id, osm_way_id, from_node_id, to_node_id)
  select n.outing_id, n.osm_way_id, n.from_node_id, n.to_node_id
    from tmp_near n
   cross join lateral sample_line(n.geom, c_sample_m) x
   group by n.outing_id, n.id, n.osm_way_id, n.from_node_id, n.to_node_id
  having sum(x.weight) filter (where exists (
             select 1 from tmp_track_pieces t
              where t.outing_id = n.outing_id
                and ST_DWithin(t.geom, x.point, c_within_m)
                and abs(sin(t.azimuth - x.azimuth)) < c_max_sin)) >= c_most * sum(x.weight)
  on conflict do nothing;   -- two Segments of one way with the same ends

  -- What each Outing covered, as lines with a spatial index, for the samples below.
  create temporary table tmp_covered on commit drop as
  select c.outing_id, s.geom
    from app.outing_coverage c
    join network.segments s
      on s.osm_way_id = c.osm_way_id and s.from_node_id = c.from_node_id and s.to_node_id = c.to_node_id
   where p_outings is null or c.outing_id = any (p_outings);
  create index on tmp_covered using gist (geom);
  analyze tmp_covered;

  -- The track sampled finely: each sample far from every Segment, or beside a covered one.
  create temporary table tmp_track on commit drop as
  select o.id as outing_id, x.ord, x.point, x.weight,
         not exists (select 1 from network.segments s where ST_DWithin(s.geom, x.point, c_gap_m)) as far,
         exists (select 1 from tmp_covered c
                  where c.outing_id = o.id and ST_DWithin(c.geom, x.point, c_within_m)) as beside_covered
    from app.outings o
   cross join lateral sample_line(o.geom, c_track_sample_m) x
   where p_outings is null or o.id = any (p_outings);

  -- Gaps: runs of consecutive far samples, each joined into a line.
  insert into app.outing_gaps (outing_id, geom, length_m)
  select outing_id, ST_MakeLine(point order by ord), sum(weight)
    from (select outing_id, ord, point, weight,
                 ord - row_number() over (partition by outing_id order by ord) as run
            from tmp_track
           where far) far_samples
   group by outing_id, run
  having sum(weight) >= c_least_gap_m and count(*) >= 2;

  update app.outings o
     set coverage = shares.coverage
    from (select outing_id, coalesce(sum(weight) filter (where beside_covered), 0) / sum(weight) as coverage
            from tmp_track
           group by outing_id) shares
   where o.id = shares.outing_id;
end;
$$;
