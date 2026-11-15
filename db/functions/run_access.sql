-- Whether a Run may use an OSM way, from its tags (PLAN T004; CONTEXT.md: Access).
-- Repeatable: Migrations applies every script in db/functions after the numbered migrations, on
-- every run, so a function is changed by editing it here (create or replace). Schema changes
-- still go in numbered migrations.

-- A pure function over the tags, like classify_ground().
create or replace function run_access(p_tags jsonb)
returns access_kind
language plpgsql
immutable
security invoker
set search_path = public, pg_temp
as $$
declare
  v_foot   text := p_tags ->> 'foot';
  v_access text := p_tags ->> 'access';
begin
  -- No one may walk on a motorway, whatever its tags say (kept for drive times, T035).
  if p_tags ->> 'highway' in ('motorway', 'motorway_link') then
    return 'forbidden';
  end if;

  -- An explicit foot tag decides; otherwise a general prohibition applies to walkers too.
  -- access=destination and similar restrict vehicles, not feet.
  if v_foot in ('no', 'private') then
    return 'forbidden';
  end if;
  if v_foot in ('yes', 'designated', 'permissive') then
    return 'allowed';
  end if;
  if v_access in ('no', 'private') then
    return 'forbidden';
  end if;
  -- A driveway is private unless its tags say otherwise (T081: a Loop went "down someone's drive
  -- where there was no public path", user, 2026-11-08); access=destination means its visitors.
  if p_tags ->> 'highway' = 'service' and p_tags ->> 'service' = 'driveway' and coalesce(v_access, '') not in ('yes', 'permissive') then
    return 'forbidden';
  end if;
  return 'allowed';
end;
$$;
