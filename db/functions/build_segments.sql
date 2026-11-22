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
  -- Short gaps (#150, T146a): joined under c_gap_joined_m, possible up to c_gap_most_m (the user's
  -- look at sample gaps, 2026-11-18); met at the other way's own point if no more than c_gap_snap_m
  -- further than the nearest spot; not where the two ways already meet within c_gap_spur_m along them.
  c_gap_joined_m constant double precision := 5;
  c_gap_most_m   constant double precision := 15;
  c_gap_snap_m   constant double precision := 1;
  c_gap_spur_m   constant double precision := 50;
  -- Nor where the path runs beside the way: still within reach of it this far back along the path.
  c_gap_beside_m constant double precision := 20;
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

  -- Crossing points mapped on a Main Road itself (#149, T145a), and their kind: each is a place to
  -- cross, so the road is cut there and a crossing can be charged at it. Lights for vehicles only
  -- (crossing=no) aren't one; junction lights with no crossing tag have a green man, as a rule.
  create temporary table tmp_main_crossings on commit drop as
  select distinct on (p.node_id) p.node_id, p.kind
    from (select node_id,
                 case when tags->>'crossing' = 'no' then null
                      when tags->>'crossing' in ('traffic_signals', 'pelican', 'puffin', 'toucan') then 'signals'
                      when tags->>'highway' = 'traffic_signals' and tags->>'crossing' is null then 'signals'
                      when tags->>'highway' = 'traffic_signals' then null
                      when tags->>'crossing' is null or tags->>'crossing' in ('zebra', 'marked', 'uncontrolled', 'zebra;dots') then 'marked'
                      else 'unmarked' end as kind
            from osm.points
           where tags->>'highway' in ('crossing', 'traffic_signals')) p
    join tmp_vertices v on v.node_id = p.node_id
    join osm.ways w on w.way_id = v.way_id
   where p.kind is not null
     and w.tags->>'highway' in ('motorway', 'motorway_link', 'trunk', 'trunk_link',
                                'primary', 'primary_link', 'secondary', 'secondary_link')
   order by p.node_id, p.kind desc;
  create unique index on tmp_main_crossings (node_id);
  analyze tmp_main_crossings;

  -- A junction is a node used more than once among routable ways (forbidden ones included:
  -- they still connect the Network). Buildings and other non-routable ways never count. A way
  -- that comes back to one of its own nodes makes that node a junction too.
  create temporary table tmp_cuts on commit drop as
  select u.*,
         (u.idx = 1 or u.idx = u.n or u.uses > 1
          or exists (select 1 from tmp_main_crossings m where m.node_id = u.node_id)) as is_cut
    from (select v.*, count(*) over (partition by v.node_id) as uses from tmp_vertices v) u;
  analyze tmp_cuts;

  -- Short gaps (#150, T146a, STORY-051): a path's dead end, a node no other way uses, is joined to
  -- the nearest way within c_gap_most_m that it isn't already joined to nearby, unless a barrier,
  -- waterway or railway is mapped across. A road that dead-ends is left alone. The gap meets that
  -- way at its own point if that is no more than c_gap_snap_m further than the nearest spot on it,
  -- or else at that spot, a new point numbered minus the dead end's node id, so it's the same point
  -- on every build (ADR 0005). Under c_gap_joined_m it's joined; up to c_gap_most_m, possible.
  create temporary table tmp_gaps on commit drop as
  with dead as (
    select c.way_id, c.node_id, ST_Transform(c.point, 27700) as point, c.point as point_4326, w.nodes, ST_Transform(w.geom, 27700) as line
      from tmp_cuts c
      join osm.ways w on w.way_id = c.way_id
     where (c.idx = 1 or c.idx = c.n) and c.uses = 1
       and classify_ground(w.tags)::text in ('off_road', 'traffic_free')
       and run_access(w.tags)::text <> 'forbidden'
  ), near as (
    select d.*, o.way_id as onto_way_id, o.nodes as onto_nodes, o.line as onto_line,
           ST_Distance(o.line, d.point) as spot_m, ST_ClosestPoint(o.line, d.point) as spot
      from dead d
     cross join lateral (
       select w.way_id, w.nodes, ST_Transform(w.geom, 27700) as line
         from osm.ways w
        where w.geom && ST_Expand(d.point_4326, 0.0004)
          and w.way_id <> d.way_id
          and ST_NPoints(w.geom) = cardinality(w.nodes)
          and run_access(w.tags)::text <> 'forbidden'
          and ST_DWithin(ST_Transform(w.geom, 27700), d.point, c_gap_most_m)
        order by ST_Distance(ST_Transform(w.geom, 27700), d.point)
        limit 1) o
  ), placed as (
    select n.*, p.path[1] as onto_idx, p.geom as vertex, ST_Distance(p.geom, n.point) as vertex_m,
           e.path[1] as edge
      from near n
     cross join lateral (select q.path, q.geom from ST_DumpPoints(n.onto_line) q order by q.geom <-> n.point limit 1) p
     cross join lateral (select q.path from ST_DumpSegments(n.onto_line) q order by ST_Distance(q.geom, n.point) limit 1) e
  ), chosen as (
    select p.way_id, p.node_id, p.point, p.nodes, p.line, p.onto_way_id, p.onto_nodes, p.onto_line,
           p.vertex_m <= p.spot_m + c_gap_snap_m as at_vertex,
           case when p.vertex_m <= p.spot_m + c_gap_snap_m then p.onto_nodes[p.onto_idx] else -p.node_id end as onto_node_id,
           case when p.vertex_m <= p.spot_m + c_gap_snap_m then p.onto_idx end as onto_idx,
           p.edge,
           case when p.vertex_m <= p.spot_m + c_gap_snap_m then p.vertex else p.spot end as onto_point
      from placed p
  )
  select c.way_id, c.node_id, c.onto_way_id, c.onto_node_id, c.onto_idx, c.edge, c.at_vertex, c.onto_line,
         c.onto_point, ST_MakeLine(c.point, c.onto_point) as geom, ST_Distance(c.point, c.onto_point) as gap_m
    from chosen c
   where ST_Distance(c.point, c.onto_point) between 0.01 and c_gap_most_m
     -- Not back to a point of its own path: a path round a field to near where it left its way would
     -- be a second Segment of the path between the same two points (the Great Britain build failed on one).
     and c.onto_node_id <> all (c.nodes)
     -- Nothing mapped across: a fence, wall, hedge, waterway or railway (as Open ground, T048).
     and not exists (select 1 from osm.lines l
                      where l.geom && ST_Transform(ST_MakeLine(c.point, c.onto_point), 4326)
                        and ST_Intersects(l.geom, ST_Transform(ST_MakeLine(c.point, c.onto_point), 4326)))
     -- Not already joined nearby: a spur off the same way, back no further than c_gap_spur_m along the two.
     and not exists (select 1
                       from unnest(c.nodes) s (node_id)
                      cross join lateral (select ST_PointN(c.onto_line, array_position(c.onto_nodes, s.node_id)) as at) v
                      where v.at is not null
                        and abs(ST_LineLocatePoint(c.line, v.at) - ST_LineLocatePoint(c.line, c.point)) * ST_Length(c.line)
                          + abs(ST_LineLocatePoint(c.onto_line, v.at) - ST_LineLocatePoint(c.onto_line, c.onto_point)) * ST_Length(c.onto_line)
                          <= c_gap_spur_m)
     -- Not a path beside the way, as a pavement ends beside its road: c_gap_beside_m back along it, it's
     -- still within reach of the way. Beside, it's the way's own ground (same ground), no new way through.
     and not ST_DWithin(c.onto_line,
                        ST_LineInterpolatePoint(c.line, case when c.node_id = c.nodes[1] then least(1, c_gap_beside_m / ST_Length(c.line))
                                                             else greatest(0, 1 - c_gap_beside_m / ST_Length(c.line)) end),
                        c_gap_most_m);
  -- Two paths ending end to end find each other: one gap, from the lower node.
  delete from tmp_gaps g
   using tmp_gaps r
   where r.node_id = g.onto_node_id and r.onto_node_id = g.node_id and r.node_id < g.node_id;

  -- The way each gap meets is cut there: at its own point, or at the new one, put in between its
  -- points and the way's points numbered afresh.
  update tmp_cuts c
     set is_cut = true
    from tmp_gaps g
   where g.at_vertex and c.way_id = g.onto_way_id and c.idx = g.onto_idx;
  with moved as (
    delete from tmp_cuts c
     where c.way_id in (select g.onto_way_id from tmp_gaps g where not g.at_vertex)
    returning c.*
  ), placed as (
    select way_id, idx::double precision as at, node_id, point, uses, is_cut from moved
    union all
    select g.onto_way_id, g.edge + ST_LineLocatePoint(e.geom, g.onto_point), g.onto_node_id, ST_Transform(g.onto_point, 4326), 1, true
      from tmp_gaps g
     -- The way's line kept from finding the gap: osm.ways has no index on way_id, and one look-up
     -- for each gap read all of Great Britain's ways (the build of 2026-11-18 ran 6 hours long).
     cross join lateral (select q.geom from ST_DumpSegments(g.onto_line) q
                          where q.path[1] = g.edge) e
     where not g.at_vertex
  )
  insert into tmp_cuts (way_id, idx, node_id, point, n, uses, is_cut)
  select way_id, row_number() over (partition by way_id order by at), node_id, point, count(*) over (partition by way_id), uses, is_cut
    from placed;
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
                                     crossing, crossing_main_road, crossing_share, bridge_over_water, access_trail, closed_months, busy, carriageway, from_bearing, to_bearing, pavement, customers)
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
         motor_closed_months(w.tags)::smallint,
         -- Busy (T145a): crossing it with no crossing point is never chosen. A trunk road, four lanes
         -- or more, or a primary road one way (a carriageway of a dual carriageway).
         coalesce(w.tags->>'highway' in ('motorway', 'motorway_link', 'trunk', 'trunk_link')
                  or (w.tags->>'highway' in ('primary', 'secondary') and w.tags->>'lanes' ~ '^[0-9]+$' and (w.tags->>'lanes')::int >= 4)
                  or (w.tags->>'highway' = 'primary' and w.tags->>'oneway' in ('yes', '1', '-1')), false),
         -- A Main Road's carriageway itself (T145a), whose sides are what's crossed.
         coalesce(w.tags->>'highway' in ('motorway', 'motorway_link', 'trunk', 'trunk_link',
                                         'primary', 'primary_link', 'secondary', 'secondary_link'), false),
         -- Which way it leaves each end (T145c), 5 m along its line (or all of a shorter one): what side
         -- of a Main Road it joins. ST_Azimuth is in radians clockwise from grid north.
         degrees(ST_Azimuth(ST_StartPoint(f.geom), ST_LineInterpolatePoint(f.geom, least(1, 5 / nullif(ST_Length(f.geom), 0))))),
         degrees(ST_Azimuth(ST_EndPoint(f.geom), ST_LineInterpolatePoint(f.geom, greatest(0, 1 - 5 / nullif(ST_Length(f.geom), 0))))),
         -- A Main Road with a real pavement (T181): pavements beside one get it in group_same_ground().
         coalesce(w.tags->>'highway' in ('trunk', 'trunk_link', 'primary', 'primary_link', 'secondary', 'secondary_link'), false)
           and has_pavement(w.tags),
         -- For customers only (T182): used, striped and costed a little more.
         has_customers(w.tags)
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

  -- The gaps (T146a), where their path and the way they meet were kept: each is its path's ground
  -- and its path's to use, and nothing more (no Corridor, Crossing or Main Road).
  insert into network_next.segments (osm_way_id, from_node_id, to_node_id, geom, ground, access_run, access_ride, greenery, waterside, corridor, main_road,
                                     crossing, crossing_main_road, crossing_share, bridge_over_water, access_trail, closed_months, busy, carriageway, from_bearing, to_bearing, gap)
  select distinct on (g.node_id)
         g.way_id, g.node_id, g.onto_node_id, g.geom, p.ground, p.access_run, p.access_ride, p.greenery, p.waterside, null, false,
         null, false, 0, false, p.access_trail, p.closed_months, false, false,
         degrees(ST_Azimuth(ST_StartPoint(g.geom), ST_EndPoint(g.geom))), degrees(ST_Azimuth(ST_EndPoint(g.geom), ST_StartPoint(g.geom))),
         case when g.gap_m < c_gap_joined_m then 'joined' else 'possible' end
    from tmp_gaps g
    join network_next.segments p on p.osm_way_id = g.way_id and g.node_id in (p.from_node_id, p.to_node_id) and p.gap is null
   where exists (select 1 from network_next.segments o where o.osm_way_id = g.onto_way_id and g.onto_node_id in (o.from_node_id, o.to_node_id))
   order by g.node_id;
  select count(*) filter (where gap = 'joined'), count(*) filter (where gap = 'possible')
    into v_result.gaps_joined, v_result.gaps_possible
    from network_next.segments where gap is not null;

  -- The crossing points on Main Roads that made it into the Network, now junctions (T145a).
  insert into network_next.crossing_nodes (node_id, kind)
  select m.node_id, m.kind
    from tmp_main_crossings m
   where exists (select 1 from network_next.segments s where s.main_road and s.from_node_id = m.node_id)
      or exists (select 1 from network_next.segments s where s.main_road and s.to_node_id = m.node_id);

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
