-- Whether a point or line (British National Grid) is in England or Wales, any part of it (PLAN
-- T030): a Request's start, an Outing's track. Always true on a Network that isn't clipped to them
-- (built from a regional extract): there's nothing to test against, and nothing is refused.
-- Repeatable: Migrations applies every script in db/functions after the numbered migrations, on
-- every run, so a function is changed by editing it here (create or replace). Schema changes
-- still go in numbered migrations.
create or replace function in_england_and_wales(p_geom geometry)
returns boolean
language sql
stable
as $$
  select not exists (select 1 from network.england_and_wales)
      or exists (select 1 from network.england_and_wales e where ST_Intersects(e.geom, p_geom))
$$;
