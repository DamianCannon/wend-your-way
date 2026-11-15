-- The Obstacles on every Segment of the Network in p_schema (network_next in a build, network on
-- its own), in place of any found before (PLAN T069; CONTEXT.md: Obstacle). A barrier node of a
-- kind that slows a bike, inside a Segment (between its two junctions on its way), is the
-- Segment's; one on a junction is half each Segment's that ends there, so a Loop passing through
-- pays once. A way tagged ford is a ford on each of its Segments. Swing, wicket, sliding and
-- Hampshire gates are gates; bollards, lift gates and the like are left out: a bike rides through.
-- Returns how many of each kind were found.
-- Repeatable: Migrations applies every script in db/functions after the numbered migrations, on
-- every run, so a function is changed by editing it here (create or replace).
create or replace function find_obstacles(p_schema text)
returns table (kind text, found bigint)
language plpgsql
volatile
security invoker
set search_path = public, pg_temp
as $$
begin
  if p_schema not in ('network', 'network_next') then
    raise exception 'find_obstacles: only network or network_next, not %', p_schema;
  end if;

  execute format('truncate %I.segment_obstacles', p_schema);

  create temp table obstacle_nodes on commit drop as
  select p.node_id,
         case p.tags->>'barrier'
           when 'swing_gate' then 'gate' when 'wicket_gate' then 'gate'
           when 'sliding_gate' then 'gate' when 'hampshire_gate' then 'gate'
           else p.tags->>'barrier'
         end as kind
    from osm.points p
   where p.tags->>'barrier' in ('gate', 'swing_gate', 'wicket_gate', 'sliding_gate', 'hampshire_gate', 'cycle_barrier',
                                'kissing_gate', 'turnstile', 'motorcycle_barrier', 'stile', 'cattle_grid');
  create index on obstacle_nodes (node_id);
  analyze obstacle_nodes;

  -- A Segment's way runs from its from-node to its to-node: its inside nodes lie between them. A
  -- closed way lists its first node again at the end, so the to-node is looked for after the from.
  -- An ordinary string, quotes doubled: DbUp reads a $name$ quote as one of its variables.
  execute format('
    insert into %1$I.segment_obstacles (segment_id, kind, share)
    select segment_id, kind, sum(share)
      from (
        select s.id as segment_id, o.kind, 1.0 as share
          from %1$I.segments s
          join osm.ways w on w.way_id = s.osm_way_id
          cross join lateral (select array_position(w.nodes, s.from_node_id) as a) f
          cross join lateral (select array_position(w.nodes, s.to_node_id, f.a + 1) as b) t
          cross join lateral unnest(w.nodes[f.a + 1 : t.b - 1]) as inside(node_id)
          join obstacle_nodes o on o.node_id = inside.node_id
         where s.osm_way_id <> 0 and f.a is not null and t.b is not null
        union all
        select s.id, o.kind, 0.5
          from obstacle_nodes o
          join %1$I.segments s on s.from_node_id = o.node_id
        union all
        select s.id, o.kind, 0.5
          from obstacle_nodes o
          join %1$I.segments s on s.to_node_id = o.node_id
        union all
        select s.id, ''ford'', 1.0
          from %1$I.segments s
          join osm.ways w on w.way_id = s.osm_way_id
         where w.tags->>''ford'' in (''yes'', ''stepping_stones'')
      ) shares
     group by segment_id, kind
  ', p_schema);

  return query execute format('select kind, count(*) from %I.segment_obstacles group by kind order by kind', p_schema);
end;
$$;
