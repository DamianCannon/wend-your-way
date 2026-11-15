-- 0003: turn osm.ways (written by osm2pgsql with db/osm2pgsql/cutthrough.lua) into
-- network.segments (PLAN T004).
--
-- England-and-Wales clipping (STORY-002 AC1) is deferred to the Great Britain spike (T018);
-- a London extract doesn't need it.

-- Staging for the raw osm2pgsql output. osm2pgsql creates and replaces its own tables here.
create schema if not exists osm;

-- What one run of build_segments() did. Nothing is dropped silently: every skipped way is a
-- lost path, so the counts are reported and the CLI prints them.
create type segment_build as (
  ways_read         bigint,   -- routable ways osm2pgsql wrote
  ways_misaligned   bigint,   -- skipped: geometry vertices don't match node IDs one-to-one
  segments_written  bigint,
  identity_clashes  bigint,   -- skipped: a second Segment with the same OSM identity
  zero_length       bigint    -- skipped: both ends at the same spot
);

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
  select w.way_id, v.idx::int as idx, v.node_id,
         ST_PointN(w.geom, v.idx::int) as point,
         cardinality(w.nodes) as n,
         w.nodes[1] = w.nodes[cardinality(w.nodes)] as closed
    from osm.ways w
    cross join lateral unnest(w.nodes) with ordinality as v(node_id, idx)
   where ST_NPoints(w.geom) = cardinality(w.nodes);

  -- A junction is a node used more than once among routable ways (forbidden ones included:
  -- they still connect the Network). Buildings and other non-routable ways never count.
  create temporary table tmp_junctions on commit drop as
  select node_id from tmp_vertices group by node_id having count(*) > 1;

  -- A way is cut at its ends, at its junctions, and (if closed) at its middle vertex, so a
  -- path round a pond becomes two Segments that can be travelled rather than one from a
  -- node back to itself.
  create temporary table tmp_cuts on commit drop as
  select v.*,
         (v.idx = 1 or v.idx = v.n
          or exists (select 1 from tmp_junctions j where j.node_id = v.node_id)
          or (v.closed and v.idx = (v.n + 1) / 2)) as is_cut
    from tmp_vertices v;

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
