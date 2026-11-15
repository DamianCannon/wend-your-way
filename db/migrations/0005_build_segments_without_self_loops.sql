-- 0005: build_segments() never writes a Segment from a node back to itself (T005a).
--
-- The first real build (Greater London) wrote 892 of them, from ways that come back to one of
-- their own nodes partway along, such as a path ending in a turning loop. Any piece that
-- starts and ends at the same node is now cut at its middle vertex. That one rule also covers
-- closed ways joined at one point or none, which 0003/0004 handled as a special case: a closed
-- way's first node appears twice, so it is always a cut.

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
