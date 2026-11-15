-- Paths beside roads: the roads' ground for the metres beside them, and Connectors when paved
-- and mostly beside roads (PLAN T014; SPEC R2.4).
-- Repeatable: Migrations applies every script in db/functions after the numbered migrations, on
-- every run, so a function is changed by editing it here (create or replace). Schema changes
-- still go in numbered migrations.

-- Run straight after build_segments(), in the same transaction (NetworkBuild.Build): it fills
-- network_next.segment_beside and rewrites the ground of the paths it finds beside roads.
create or replace function group_same_ground()
returns same_ground_build
language plpgsql
volatile
security invoker
set search_path = public, pg_temp
as $$
declare
  -- Ways a Run uses away from traffic, unless they turn out to be beside it.
  c_paths     constant text[] := array['footway', 'cycleway', 'path', 'pedestrian', 'bridleway', 'steps', 'track'];
  -- Half of London's tagged pavements lie within 5.5 m of their road's centre line and 96%
  -- within 12 m; roadside cycle tracks are much the same.
  c_within_m  constant double precision := 12;
  -- A path is sampled every 10 m or so. A sample is beside a road when the road is within
  -- c_within_m and runs within 30° of the same way (|sin| < 0.5, either direction).
  c_sample_m  constant double precision := 10;
  c_max_sin   constant double precision := 0.5;
  -- A path is beside roads when most of its length is beside them.
  c_most      constant double precision := 0.6;
  -- Less than this beside a road Segment isn't a shared stretch.
  c_least_m   constant double precision := 5;
  v_result    same_ground_build;
begin
  -- Roads with traffic, broken into their straight pieces, each with its bearing.
  create temporary table tmp_road_pieces on commit drop as
  select s.id as road_id,
         d.geom,
         ST_Azimuth(ST_StartPoint(d.geom), ST_EndPoint(d.geom)) as azimuth
    from network_next.segments s
    join osm.ways w on w.way_id = s.osm_way_id
   cross join lateral ST_DumpSegments(s.geom) d
   where s.ground = 'connector'
     and not (w.tags ->> 'highway' = any (c_paths))
     and ST_Length(d.geom) > 0;
  create index on tmp_road_pieces using gist (geom);
  analyze tmp_road_pieces;

  -- Samples along every path (sample_line(), shared with Outing coverage).
  create temporary table tmp_samples on commit drop as
  select s.id, x.point, x.azimuth, x.weight
    from network_next.segments s
    join osm.ways w on w.way_id = s.osm_way_id
   cross join lateral sample_line(s.geom, c_sample_m) x
   where w.tags ->> 'highway' = any (c_paths);

  -- Each sample, with the nearest road running the same way beside it, if there is one.
  create temporary table tmp_beside on commit drop as
  select t.id, t.weight, near.road_id
    from tmp_samples t
    left join lateral (
      select r.road_id
        from tmp_road_pieces r
       where ST_DWithin(r.geom, t.point, c_within_m)
         and abs(sin(t.azimuth - r.azimuth)) < c_max_sin
       order by r.geom <-> t.point
       limit 1) near on true;

  -- Each path lies on every road Segment's ground it runs beside, for the metres it runs beside
  -- it. Less than c_least_m beside a road Segment is noise, not a shared stretch.
  insert into network_next.segment_beside (path_id, road_id, metres)
  select id, road_id, sum(weight)
    from tmp_beside
   where road_id is not null
   group by id, road_id
  having sum(weight) >= c_least_m
   order by id, road_id;   -- in the Segments' map order, so a slice's same ground lies together

  -- A path mostly beside roads runs beside traffic: a paved one (a pavement, a cycle track)
  -- becomes a Connector; one tagged unpaved keeps its Off-road ground (user: happy on dirt paths
  -- anywhere). A path with no surface tag is as likely a shared-use tarmac path, and mapped as a
  -- footway it would be a Connector, so it becomes one too (T038). It's a Main Road's pavement if
  -- most of its metres beside roads are beside Main Roads, and on no Corridor, whatever route
  -- it's on: a Corridor is green ground (T037).
  update network_next.segments s
     set ground = case when mostly.stays then s.ground else 'connector' end,
         main_road = not mostly.stays and mostly.main_share >= 0.5,
         corridor = case when mostly.stays then s.corridor end
    -- Each path joined to its way once, after grouping: osm.ways has no index on way_id, and a
    -- lookup per path took hours on London (T038).
    from (select m.id, m.main_share, p.ground = 'off_road' and tagged_unpaved(w.tags) as stays
            from (select t.id,
                         coalesce(sum(t.weight) filter (where r.main_road), 0)
                           / nullif(sum(t.weight) filter (where t.road_id is not null), 0) as main_share
                    from tmp_beside t
                    left join network_next.segments r on r.id = t.road_id
                   group by t.id
                  having sum(t.weight) filter (where t.road_id is not null) / sum(t.weight) >= c_most) m
            join network_next.segments p on p.id = m.id
            join osm.ways w on w.way_id = p.osm_way_id) mostly
   where s.id = mostly.id;
  get diagnostics v_result.paths_beside_roads = row_count;

  return v_result;
end;
$$;
