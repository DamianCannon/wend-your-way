-- Open ground (PLAN T039, STORY-038): straight Off-road Segments across public open land, into
-- network_next after same ground, so a crossing is never taken for a pavement.
-- Repeatable: Migrations applies every script in db/functions after the numbered migrations, on
-- every run, so a function is changed by editing it here (create or replace). Schema changes
-- still go in numbered migrations.

-- What an area is to Open ground: 'open' land adds to it, 'blocked' land is taken out of it,
-- and 'neutral' land neither (a pitch or a lawn counts where it's inside a park, not alone).
-- Woods are blocked, the user's choice (2026-10-23).
create or replace function open_ground_class(p_tags jsonb)
returns text
language sql
immutable
as $$
  select case
    when coalesce(p_tags->>'access', '') in ('private', 'no', 'customers')
      or coalesce(p_tags->>'foot', '') = 'customers'
      or coalesce(p_tags->>'sport', '') in ('tennis', 'basketball', 'multi', 'bowls', 'skateboard')
      or coalesce(p_tags->>'natural', '') in ('water', 'wetland', 'wood', 'scrub')
      or coalesce(p_tags->>'waterway', '') = 'riverbank'
      or coalesce(p_tags->>'leisure', '') in ('golf_course', 'garden')
      or coalesce(p_tags->>'landuse', '') in ('allotments', 'cemetery', 'forest')
      or coalesce(p_tags->>'amenity', '') in ('grave_yard', 'parking')
      -- Military land: firing ranges and training areas (Great Britain's first build crossed some).
      or coalesce(p_tags->>'landuse', '') = 'military'
      then 'blocked'
    when coalesce(p_tags->>'leisure', '') in ('park', 'common', 'recreation_ground', 'nature_reserve')
      or coalesce(p_tags->>'landuse', '') in ('recreation_ground', 'village_green')
      or coalesce(p_tags->>'natural', '') in ('heath', 'beach')
      then 'open'
    else 'neutral'
  end
$$;

drop function if exists build_open_ground();
create or replace function build_open_ground()
returns table (crossings bigint, length_m double precision)
language plpgsql
volatile
security invoker
set search_path = public, pg_temp
as $$
declare
  -- Land and fields smaller than these hold nothing worth crossing.
  c_least_land_m2   constant double precision := 2000;
  c_least_field_m2  constant double precision := 400;
  -- A crossing is at least c_least_m long, and going round the field's edge instead is at least
  -- c_detour times as far and c_least_saved_m further.
  c_least_m         constant double precision := 30;
  -- Nor more than c_most_m: upland moor is mapped as heath, and Great Britain's first build drew
  -- crossings up to 10.9 km across it. A km covers any field, common or heath a Run cuts across.
  c_most_m          constant double precision := 1000;
  c_detour          constant double precision := 1.3;
  c_least_saved_m   constant double precision := 50;
  -- A crossing whose ends are both within c_copy_m of a kept one's is a near copy of it.
  c_copy_m          constant double precision := 20;
  v_fit record;
begin
  if to_regclass('osm.areas') is null then
    return query select 0::bigint, 0::double precision;
    return;
  end if;

  create temporary table tmp_open_areas on commit drop as
  select ST_MakeValid(ST_Transform(a.geom, 27700)) as geom, open_ground_class(a.tags) as class
    from osm.areas a;
  delete from tmp_open_areas where class = 'neutral';
  create index on tmp_open_areas using gist (geom);
  analyze tmp_open_areas;

  -- Open land: open areas joined where they touch, less the blocked land on them.
  create temporary table tmp_land on commit drop as
  select p.geom
    from (select (ST_Dump(ST_Difference(c.geom,
                   coalesce((select ST_Union(b.geom) from tmp_open_areas b
                              where b.class = 'blocked' and ST_Intersects(b.geom, c.geom)),
                            ST_GeomFromText('POLYGON EMPTY', 27700))))).geom
            from (select ST_Union(o.geom) as geom
                    from (select geom, ST_ClusterIntersectingWin(geom) over () as cluster
                            from tmp_open_areas where class = 'open') o
                   group by o.cluster) c) p
   where ST_Area(p.geom) >= c_least_land_m2;

  -- Fields: the land cut up by the Network's usable ways.
  create temporary table tmp_fields on commit drop as
  select row_number() over () as field_id, f.geom
    from tmp_land l
   cross join lateral (
     select (ST_Dump(ST_Polygonize(n.g))).geom
       from (select ST_Node(ST_Collect(x.geom)) as g
               from (select (ST_Dump(ST_Boundary(l.geom))).geom
                     union all
                     select s.geom from network_next.segments s
                      where ST_Intersects(s.geom, l.geom) and s.access_run <> 'forbidden') x) n) f
   where ST_Within(ST_PointOnSurface(f.geom), l.geom) and ST_Area(f.geom) >= c_least_field_m2;
  create index on tmp_fields using gist (geom);
  analyze tmp_fields;

  -- Each field's corners: the junctions on its edge, and how far round its edge each one is.
  create temporary table tmp_corners on commit drop as
  select f.field_id, j.node_id, j.point, ST_Length(ST_ExteriorRing(f.geom)) as perimeter_m,
         ST_LineLocatePoint(ST_ExteriorRing(f.geom), j.point) as at
    from tmp_fields f
    join lateral (select distinct e.node_id, e.point
                    from network_next.segments s
                   cross join lateral (values (s.from_node_id, ST_StartPoint(s.geom)), (s.to_node_id, ST_EndPoint(s.geom))) e (node_id, point)
                   where ST_DWithin(s.geom, ST_ExteriorRing(f.geom), 1) and s.access_run <> 'forbidden'
                     and ST_DWithin(e.point, ST_ExteriorRing(f.geom), 1)) j on true;
  create index on tmp_corners (field_id);

  -- What a crossing mustn't go through (T048): barriers, railways and waterways, as lines.
  create temporary table tmp_barriers (geom geometry(LineString, 27700)) on commit drop;
  if to_regclass('osm.lines') is not null then
    insert into tmp_barriers (geom) select ST_Transform(l.geom, 27700) from osm.lines l;
  end if;
  create index on tmp_barriers using gist (geom);
  analyze tmp_barriers;

  -- Pairs of corners worth crossing between: going round the shorter way is a real detour.
  create temporary table tmp_fits on commit drop as
  select p.*
    from (select a.field_id, a.node_id as from_node_id, b.node_id as to_node_id, a.point as from_point, b.point as to_point,
                 ST_MakeLine(a.point, b.point) as geom, ST_Distance(a.point, b.point) as straight_m,
                 least(abs(a.at - b.at), 1 - abs(a.at - b.at)) * a.perimeter_m as round_m
            from tmp_corners a
            join tmp_corners b on b.field_id = a.field_id and b.node_id > a.node_id) p
    join tmp_fields f on f.field_id = p.field_id
   where p.straight_m between c_least_m and c_most_m
     and p.round_m >= c_detour * p.straight_m
     and p.round_m - p.straight_m >= c_least_saved_m
     and ST_Within(p.geom, ST_Buffer(f.geom, 1))
     -- Not through a fence, wall, hedge, railway or waterway (T048).
     and not exists (select 1 from tmp_barriers b where ST_Intersects(b.geom, p.geom));

  -- Most saved first, a crossing is kept unless it cuts across one already kept in its field
  -- (a field's crossings never cross without a junction) or is a near copy of one.
  create temporary table tmp_crossings (field_id bigint, from_node_id bigint, to_node_id bigint,
                                        from_point geometry, to_point geometry, geom geometry) on commit drop;
  create index on tmp_crossings (field_id);
  for v_fit in select * from tmp_fits order by field_id, round_m - straight_m desc, from_node_id, to_node_id loop
    if not exists (select 1 from tmp_crossings k
                    where k.field_id = v_fit.field_id
                      and (ST_Crosses(k.geom, v_fit.geom)
                        or (ST_DWithin(k.from_point, v_fit.from_point, c_copy_m) and ST_DWithin(k.to_point, v_fit.to_point, c_copy_m))
                        or (ST_DWithin(k.from_point, v_fit.to_point, c_copy_m) and ST_DWithin(k.to_point, v_fit.from_point, c_copy_m)))) then
      insert into tmp_crossings values (v_fit.field_id, v_fit.from_node_id, v_fit.to_node_id, v_fit.from_point, v_fit.to_point, v_fit.geom);
    end if;
  end loop;

  -- No OSM way: way id 0 and its junctions' node ids are its identity (ADR 0005). In map order
  -- among themselves, after the ways' Segments.
  -- A bike across open land: whether it may is the land's own rule, which the map doesn't say. A
  -- motorbike may not: open land gives no motor vehicle a right (T021).
  insert into network_next.segments (osm_way_id, from_node_id, to_node_id, geom, ground, access_run, access_ride, access_trail, closed_months, greenery, corridor, main_road)
  select distinct on (c.from_node_id, c.to_node_id)
         0, c.from_node_id, c.to_node_id, c.geom, 'off_road', 'allowed', 'unconfirmed', 'forbidden', 0,
         1,   -- the land is all green areas
         null, false
    from tmp_crossings c
   order by c.from_node_id, c.to_node_id;

  return query select count(*), coalesce(sum(ST_Length(c.geom)), 0) from network_next.segments c where c.osm_way_id = 0;
end;
$$;
