-- Points spread evenly along a line, each with the bearing of the straight piece it lies on and
-- the share of the line's length it stands for (PLAN T014, T016). Same ground and Outing coverage
-- both compare lines this way, so they measure alike.
-- Repeatable: Migrations applies every script in db/functions after the numbered migrations, on
-- every run, so a function is changed by editing it here (create or replace). Schema changes
-- still go in numbered migrations.

-- Each straight piece of the line gets ceil(length / spacing) points, at least one, at the
-- middles of equal parts, so the weights add up to the line's length.
create or replace function sample_line(p_geom geometry, p_spacing_m double precision)
returns table (ord bigint, point geometry, azimuth double precision, weight double precision)
language sql
immutable
parallel safe
-- No "set search_path": PostgreSQL inlines a SQL function into the query that calls it only
-- when it has no SET clause: on London, same ground took 66 s inlined against 77 s not.
as $$
  select row_number() over (order by d.path[1], i),
         ST_LineInterpolatePoint(d.geom, (i + 0.5) / pieces.n),
         ST_Azimuth(ST_StartPoint(d.geom), ST_EndPoint(d.geom)),
         ST_Length(d.geom) / pieces.n
    from ST_DumpSegments(p_geom) d
   cross join lateral (select greatest(1, ceil(ST_Length(d.geom) / p_spacing_m))::int as n) pieces
   cross join lateral generate_series(0, pieces.n - 1) i
   where ST_Length(d.geom) > 0
$$;
