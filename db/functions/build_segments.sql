-- Cuts osm.ways into the Network's Segments at junctions (PLAN T004, T005, T005a), into the
-- empty network_next that prepare_network_next() made (T034): the app keeps reading network.
-- Repeatable: Migrations applies every script in db/functions after the numbered migrations, on
-- every run, so a function is changed by editing it here (create or replace). Schema changes
-- still go in numbered migrations.

-- A Segment ends only at a junction: a node used more than once among routable ways. A piece
-- that would start and end at the same node (a closed way, a turning loop at the end of a path)
-- is also cut at its middle vertex, so every Segment can be travelled.
create or replace function build_segments()
returns segment_build
language plpgsql
volatile
security invoker
set search_path = public, pg_temp
as $$
declare
  v_result segment_build;
  v_found text[];
  v_clip boolean;
begin
  -- England and Wales, from the extract's country relations (T030), cut small and indexed so
  -- each test is against a few hundred vertices, not a coastline; Scotland likewise, to count
  -- what's left out there. Only when England and Wales are both there whole: a regional extract
  -- holds them broken or not at all, and clipping to one country would drop the other's ground.
  -- First, so a boundary PostGIS can't use fails the build at once, not half an hour in.
  if to_regclass('osm.countries') is not null then
    select array_agg(distinct iso order by iso)
      into v_found
      from osm.countries
     where geom is not null and not ST_IsEmpty(geom);
  end if;
  v_result.countries_found := array_to_string(v_found, ',');
  v_clip := coalesce(v_found @> array['GB-ENG', 'GB-WLS'], false);
  create temporary table tmp_scotland (geom geometry(Polygon, 27700)) on commit drop;
  if v_clip then
    insert into network_next.england_and_wales (geom)
    select ST_Subdivide(ST_CollectionExtract(ST_MakeValid(ST_Transform(geom, 27700)), 3), 256)
      from osm.countries
     where iso in ('GB-ENG', 'GB-WLS') and geom is not null;
    analyze network_next.england_and_wales;

    insert into tmp_scotland (geom)
    select ST_Subdivide(ST_CollectionExtract(ST_MakeValid(ST_Transform(geom, 27700)), 3), 256)
      from osm.countries
     where iso = 'GB-SCT' and geom is not null;
    create index on tmp_scotland using gist (geom);
    analyze tmp_scotland;
  end if;

  select count(*), count(*) filter (where ST_NPoints(geom) <> cardinality(nodes))
    into v_result.ways_read, v_result.ways_misaligned
    from osm.ways;

  -- Every vertex of every aligned way, with its node ID. Ways whose geometry dropped a
  -- vertex (a node missing from the extract, or two nodes at one spot) can't be cut safely.
  create temporary table tmp_vertices on commit drop as
  select w.way_id,
         d.path[1] as idx,
         w.nodes[d.path[1]] as node_id,
         d.geom as point,
         cardinality(w.nodes) as n
    from osm.ways w
    cross join lateral ST_DumpPoints(w.geom) d
   where ST_NPoints(w.geom) = cardinality(w.nodes);
  analyze tmp_vertices;

  -- A junction is a node used more than once among routable ways (forbidden ones included:
  -- they still connect the Network). Buildings and other non-routable ways never count. A way
  -- that comes back to one of its own nodes makes that node a junction too.
  create temporary table tmp_cuts on commit drop as
  select v.*,
         (v.idx = 1 or v.idx = v.n or count(*) over (partition by v.node_id) > 1) as is_cut
    from tmp_vertices v;
  analyze tmp_cuts;

  -- A piece between consecutive cuts that starts and ends at the same node (a closed way
  -- joined at one point or none, or a turning loop at the end of a path) can't be travelled,
  -- so it is also cut at its middle vertex. The middle is never that node again: every
  -- repeat of it is already a cut.
  update tmp_cuts c
     set is_cut = true
    from (select way_id, (idx + next_idx) / 2 as idx
            from (select way_id, idx, node_id,
                         lead(idx) over (partition by way_id order by idx) as next_idx,
                         lead(node_id) over (partition by way_id order by idx) as next_node
                    from tmp_cuts
                   where is_cut) cuts
           where next_node = node_id and next_idx - idx >= 2) middles
   where c.way_id = middles.way_id and c.idx = middles.idx;

  -- Number each vertex by the cuts before it. A vertex belongs to the Segment it continues
  -- (cuts_before); a cut vertex also starts the next Segment (cuts_before + 1).
  create temporary table tmp_pieces on commit drop as
  with numbered as (
    select c.*,
           coalesce(sum(case when c.is_cut then 1 else 0 end)
                      over (partition by c.way_id order by c.idx
                            rows between unbounded preceding and 1 preceding), 0) as cuts_before
      from tmp_cuts c
  ), membership as (
    select way_id, cuts_before as piece, idx, node_id, point from numbered where cuts_before >= 1
    union all
    select way_id, cuts_before + 1, idx, node_id, point from numbered where is_cut
  )
  select way_id,
         piece,
         (array_agg(node_id order by idx))[1] as from_node_id,
         (array_agg(node_id order by idx desc))[1] as to_node_id,
         ST_Transform(ST_MakeLine(point order by idx), 27700) as geom
    from membership
   group by way_id, piece
  having count(*) >= 2;
  analyze tmp_pieces;

  select count(*) filter (where ST_Length(geom) = 0) into v_result.zero_length from tmp_pieces;


  -- One piece per OSM identity, and whether it touches England or Wales: a Segment across the
  -- border is kept, so a Loop near it can still use it.
  create temporary table tmp_first on commit drop as
  select distinct on (p.way_id, p.from_node_id, p.to_node_id)
         p.way_id, p.from_node_id, p.to_node_id, p.geom,
         (not v_clip or exists (select 1 from network_next.england_and_wales e where ST_Intersects(e.geom, p.geom))) as kept
    from tmp_pieces p
   where ST_Length(p.geom) > 0
   order by p.way_id, p.from_node_id, p.to_node_id, p.piece;

  -- Greenery (T036): the share of each Segment's length, sampled every 20 m, within 15 m of green
  -- areas, cut small and indexed. Worked out here, before the insert, so no mass update moves rows
  -- out of map order. None where the extract brought no areas (an import before T035).
  create temporary table tmp_green_pieces (geom geometry(Polygon, 27700)) on commit drop;
  if to_regclass('osm.areas') is not null then
    insert into tmp_green_pieces (geom)
    select ST_Subdivide(ST_CollectionExtract(ST_MakeValid(ST_Transform(a.geom, 27700)), 3), 256)
      from osm.areas a
     where a.tags->>'leisure' in ('park', 'garden', 'nature_reserve', 'common', 'recreation_ground', 'pitch', 'golf_course')
        or a.tags->>'landuse' in ('forest', 'grass', 'meadow', 'recreation_ground', 'village_green', 'cemetery', 'allotments', 'orchard')
        or a.tags->>'natural' in ('wood', 'heath', 'grassland', 'scrub', 'moor', 'fell', 'wetland', 'beach', 'water')
        or a.tags->>'waterway' = 'riverbank'
        or a.tags->>'amenity' = 'grave_yard';
  end if;
  create index on tmp_green_pieces using gist (geom);
  analyze tmp_green_pieces;

  -- Waterside (T043): likewise, the share within 25 m of water, from the same samples. A sewage
  -- works' tanks aren't water anyone runs beside.
  create temporary table tmp_water_pieces (geom geometry(Polygon, 27700)) on commit drop;
  if to_regclass('osm.areas') is not null then
    insert into tmp_water_pieces (geom)
    select ST_Subdivide(ST_CollectionExtract(ST_MakeValid(ST_Transform(a.geom, 27700)), 3), 256)
      from osm.areas a
     where (a.tags->>'natural' = 'water' and coalesce(a.tags->>'water', '') <> 'wastewater')
        or a.tags->>'waterway' = 'riverbank';
  end if;
  -- Rivers and canals mapped only as lines count too (T048); streams and ditches don't.
  if to_regclass('osm.lines') is not null then
    insert into tmp_water_pieces (geom)
    select ST_Buffer(ST_Transform(l.geom, 27700), 0.5)
      from osm.lines l
     where l.tags->>'waterway' in ('river', 'canal');
  end if;
  create index on tmp_water_pieces using gist (geom);
  analyze tmp_water_pieces;

  create temporary table tmp_greenery on commit drop as
  select f.way_id, f.from_node_id, f.to_node_id,
         least(1, coalesce(sum(x.weight) filter (where exists (
             select 1 from tmp_green_pieces g where ST_DWithin(g.geom, x.point, 15))), 0) / sum(x.weight)) as greenery,
         least(1, coalesce(sum(x.weight) filter (where exists (
             select 1 from tmp_water_pieces w where ST_DWithin(w.geom, x.point, 25))), 0) / sum(x.weight)) as waterside
    from tmp_first f
   cross join lateral sample_line(f.geom, 20) x
   where f.kept
   group by f.way_id, f.from_node_id, f.to_node_id;
  create unique index on tmp_greenery (way_id, from_node_id, to_node_id);
  analyze tmp_greenery;

  -- Corridors (T037): each way's walking route, or national or international cycle route, by name
  -- (else ref), taking the longest where a way is in several. Local and regional cycle routes are
  -- mostly fragments on quiet streets. None where the extract brought no routes (before T035).
  create temporary table tmp_corridors (way_id bigint primary key, corridor text not null) on commit drop;
  if to_regclass('osm.routes') is not null then
    insert into tmp_corridors (way_id, corridor)
    select distinct on (m.way_id) m.way_id, r.corridor
      from (select r.relation_id, coalesce(r.tags->>'name', r.tags->>'ref') as corridor,
                   sum(ST_Length(ST_Transform(w.geom, 27700))) as length_m
              from osm.routes r
              join osm.route_ways m on m.relation_id = r.relation_id
              join osm.ways w on w.way_id = m.way_id
             where (r.tags->>'route' in ('hiking', 'foot', 'walking', 'running')
                    or (r.tags->>'route' in ('bicycle', 'mtb') and r.tags->>'network' in ('ncn', 'icn')))
               and coalesce(r.tags->>'name', r.tags->>'ref') is not null
             group by r.relation_id, 2) r
      join osm.route_ways m on m.relation_id = r.relation_id
     order by m.way_id, r.length_m desc, r.corridor;
  end if;
  analyze tmp_corridors;

  -- Crossings (T044): the pieces of crossing ways, the kind of crossing, whether the way meets or
  -- crosses a Main Road's carriageway, and each piece's share of its way by length.
  create temporary table tmp_crossing on commit drop as
  select f.way_id, f.from_node_id, f.to_node_id,
         case when w.tags->>'crossing' in ('traffic_signals', 'pelican', 'puffin', 'toucan') then 'signals'
              -- A crossing with no kind tagged: someone mapped one there, so probably a proper
              -- one, not the worst case (user, 2026-10-24).
              when w.tags->>'crossing' is null or w.tags->>'crossing' in ('zebra', 'marked', 'zebra;dots') then 'marked'
              else 'unmarked' end as kind,
         exists (select 1 from osm.ways r
                  where r.geom && w.geom and r.way_id <> w.way_id and ST_Intersects(r.geom, w.geom)
                    and r.tags->>'highway' in ('motorway', 'motorway_link', 'trunk', 'trunk_link',
                                               'primary', 'primary_link', 'secondary', 'secondary_link')) as main_road,
         ST_Length(f.geom) / sum(ST_Length(f.geom)) over (partition by f.way_id) as share
    from tmp_first f
    join osm.ways w on w.way_id = f.way_id
   where f.kept and 'crossing' in (w.tags->>'footway', w.tags->>'cycleway', w.tags->>'path');
  create unique index on tmp_crossing (way_id, from_node_id, to_node_id);
  analyze tmp_crossing;

  -- Trail Rides (T021): the pieces along a Definitive Map byway, 70% of their length within 20 m
  -- of one (checked against Wiltshire's and Hertfordshire's whole Definitive Maps).
  create temporary table tmp_on_byway on commit drop as
  select f.way_id, f.from_node_id, f.to_node_id
    from tmp_first f
   where f.kept
     and exists (select 1 from rights_of_way.byways b where ST_DWithin(b.geom, f.geom, 20))
     and (select ST_Length(ST_Intersection(f.geom, ST_Buffer(ST_Collect(b.geom), 20)))
            from rights_of_way.byways b where ST_DWithin(b.geom, f.geom, 20)) >= 0.7 * ST_Length(f.geom);
  create unique index on tmp_on_byway (way_id, from_node_id, to_node_id);
  analyze tmp_on_byway;

  -- In map order (PostGIS sorts geometries along a Hilbert curve), so ids and table pages
  -- follow the ground: a slice then reads neighbouring pages, not pages from all over Great
  -- Britain (T018).
  insert into network_next.segments (osm_way_id, from_node_id, to_node_id, geom, ground, access_run, access_ride, greenery, waterside, corridor, main_road,
                                     crossing, crossing_main_road, crossing_share, bridge_over_water, access_trail, closed_months)
  select f.way_id, f.from_node_id, f.to_node_id, f.geom, k.ground, run_access(w.tags), ride_access(w.tags), coalesce(g.greenery, 0), coalesce(g.waterside, 0),
         -- A Corridor is green ground: a road on a walking route is just a road.
         case when k.ground <> 'connector' then c.corridor end,
         -- A Main Road: heavy traffic (R2.1). Pavements beside one get it in group_same_ground().
         coalesce(w.tags->>'highway' in ('motorway', 'motorway_link', 'trunk', 'trunk_link',
                                         'primary', 'primary_link', 'secondary', 'secondary_link'), false),
         x.kind, coalesce(x.main_road, false), coalesce(x.share, 0),
         -- A bridge over water (T046), where a Loop along a river can cross back.
         coalesce(w.tags->>'bridge', 'no') <> 'no'
           and exists (select 1 from tmp_water_pieces p where ST_Intersects(p.geom, f.geom)),
         -- OSM's byway is Unconfirmed only where no Definitive Map is open.
         trail_access(w.tags, b.way_id is not null,
                      case when w.tags->>'designation' ~ '^(byway_open_to_all_traffic|public_byway)'
                           then not exists (select 1 from rights_of_way.without_data u where ST_Intersects(u.geom, f.geom))
                           else true end),
         motor_closed_months(w.tags)::smallint
    from tmp_first f
    join osm.ways w on w.way_id = f.way_id
   cross join lateral (select classify_ground(w.tags) as ground) k
    left join tmp_corridors c on c.way_id = f.way_id
    left join tmp_greenery g on (g.way_id, g.from_node_id, g.to_node_id) = (f.way_id, f.from_node_id, f.to_node_id)
    left join tmp_crossing x on (x.way_id, x.from_node_id, x.to_node_id) = (f.way_id, f.from_node_id, f.to_node_id)
    left join tmp_on_byway b on (b.way_id, b.from_node_id, b.to_node_id) = (f.way_id, f.from_node_id, f.to_node_id)
   where f.kept
   order by f.geom;
  get diagnostics v_result.segments_written = row_count;

  select count(*) - (select count(*) from tmp_first) - v_result.zero_length
    into v_result.identity_clashes
    from tmp_pieces;

  -- What was left out, and where: anything but Scotland means the boundary missed some ground.
  if v_clip then
    select count(*) filter (where in_scotland), count(*) filter (where not in_scotland)
      into v_result.in_scotland, v_result.elsewhere
      from (select exists (select 1 from tmp_scotland c where ST_Intersects(c.geom, f.geom)) as in_scotland
              from tmp_first f
             where not f.kept) outside;
  end if;

  return v_result;
end;
$$;
