-- 0004: build_segments(), revised before its first real run (Greater London, T005).
--
-- * A closed way is cut at its middle only when it has at most one junction (a path round a
--   pond joined at one point). A loop already joined at two or more points is cut only at
--   its junctions: a Segment should end only where there is a choice.
-- * Built for millions of vertices: one pass of ST_DumpPoints per way instead of ST_PointN
--   per vertex (which can re-read a long geometry every time); junctions from a window
--   count rather than a correlated lookup; statistics refreshed on every temporary table,
--   which autovacuum never analyses.

create or replace function build_segments()
returns segment_build
language plpgsql
volatile
security invoker
set search_path = public, pg_temp
as $$
declare
  v_result segment_build;
begin
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
         cardinality(w.nodes) as n,
         w.nodes[1] = w.nodes[cardinality(w.nodes)] as closed
    from osm.ways w
    cross join lateral ST_DumpPoints(w.geom) d
   where ST_NPoints(w.geom) = cardinality(w.nodes);
  analyze tmp_vertices;

  -- A junction is a node used more than once among routable ways (forbidden ones included:
  -- they still connect the Network). Buildings and other non-routable ways never count.
  create temporary table tmp_cuts on commit drop as
  with flagged as (
    select v.*, count(*) over (partition by v.node_id) > 1 as is_junction
      from tmp_vertices v
  ), junctions_per_way as (
    select way_id, count(distinct node_id) filter (where is_junction) as junctions
      from flagged
     group by way_id
  )
  select f.*,
         (f.idx = 1 or f.idx = f.n or f.is_junction
          or (f.closed and j.junctions <= 1 and f.idx = (f.n + 1) / 2)) as is_cut
    from flagged f
    join junctions_per_way j using (way_id);
  analyze tmp_cuts;

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

  delete from network.segments;

  insert into network.segments (osm_way_id, from_node_id, to_node_id, geom, ground, access_run)
  select distinct on (p.way_id, p.from_node_id, p.to_node_id)
         p.way_id, p.from_node_id, p.to_node_id, p.geom,
         classify_ground(w.tags), run_access(w.tags)
    from tmp_pieces p
    join osm.ways w on w.way_id = p.way_id
   where ST_Length(p.geom) > 0
   order by p.way_id, p.from_node_id, p.to_node_id, p.piece;
  get diagnostics v_result.segments_written = row_count;

  select count(*) - v_result.segments_written - v_result.zero_length
    into v_result.identity_clashes
    from tmp_pieces;

  return v_result;
end;
$$;
