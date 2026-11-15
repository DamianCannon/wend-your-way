-- 0007: a dirt path beside a road stays Off-road (PLAN T014).
--
-- 0006 made every path beside a road a Connector. The user is happy running dirt paths almost
-- anywhere, and many in London run beside a road; it's paved pavements and cycle tracks beside
-- roads they'd rather avoid. A dirt path beside a road is still the road's ground, so out along
-- one and back along the other is still a Retrace.

-- Run straight after build_segments(): it rewrites the ground of the paths it finds.
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
  -- A path is beside a road when most of its length is beside one.
  c_most      constant double precision := 0.6;
  v_result    same_ground_build;
begin
  -- Roads with traffic, broken into their straight pieces, each with its bearing.
  create temporary table tmp_road_pieces on commit drop as
  select s.id as road_id,
         d.geom,
         ST_Azimuth(ST_StartPoint(d.geom), ST_EndPoint(d.geom)) as azimuth
    from network.segments s
    join osm.ways w on w.way_id = s.osm_way_id
   cross join lateral ST_DumpSegments(s.geom) d
   where s.ground = 'connector'
     and not (w.tags ->> 'highway' = any (c_paths))
     and ST_Length(d.geom) > 0;
  create index on tmp_road_pieces using gist (geom);
  analyze tmp_road_pieces;

  -- Samples along every path, spread evenly over each straight piece, each standing for an
  -- equal share of that piece's length.
  create temporary table tmp_samples on commit drop as
  select s.id,
         ST_LineInterpolatePoint(d.geom, (i + 0.5) / n) as point,
         ST_Azimuth(ST_StartPoint(d.geom), ST_EndPoint(d.geom)) as azimuth,
         ST_Length(d.geom) / n as weight
    from network.segments s
    join osm.ways w on w.way_id = s.osm_way_id
   cross join lateral ST_DumpSegments(s.geom) d
   cross join lateral (select greatest(1, ceil(ST_Length(d.geom) / c_sample_m))::int as n) pieces
   cross join lateral generate_series(0, pieces.n - 1) i
   where w.tags ->> 'highway' = any (c_paths)
     and ST_Length(d.geom) > 0;

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

  -- A path mostly beside roads is the same ground as the road it runs beside for longest (ties
  -- to the lowest id). A paved one (a pavement, a cycle track) becomes a Connector; an Off-road
  -- one keeps its ground.
  update network.segments s
     set ground = case when s.ground = 'off_road' then s.ground else 'connector' end,
         same_ground_as = chosen.road_id
    from (select id, road_id
            from (select id, road_id,
                         sum(sum(weight)) filter (where road_id is not null) over (partition by id)
                           / sum(sum(weight)) over (partition by id) as beside_share,
                         row_number() over (partition by id order by road_id is null, sum(weight) desc, road_id) as rank
                    from tmp_beside
                   group by id, road_id) shares
           where rank = 1 and beside_share >= c_most) chosen
   where s.id = chosen.id;
  get diagnostics v_result.paths_beside_roads = row_count;

  return v_result;
end;
$$;
