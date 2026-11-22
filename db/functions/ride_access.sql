-- Whether a Gravel Ride may use an OSM way, from its tags (PLAN T068; SPEC R9.1; CONTEXT.md:
-- Access, Unconfirmed Access). Not on public footpaths, pavements, pedestrian streets or steps;
-- a bicycle tag decides where there is one; an untagged path is Unconfirmed: used, but flagged.
-- Repeatable: Migrations applies every script in db/functions after the numbered migrations, on
-- every run, so a function is changed by editing it here (create or replace).

-- A pure function over the tags, like run_access().
create or replace function ride_access(p_tags jsonb)
returns access_kind
language plpgsql
immutable
security invoker
set search_path = public, pg_temp
as $$
declare
  v_highway     text := p_tags ->> 'highway';
  v_bicycle     text := p_tags ->> 'bicycle';
  v_access      text := p_tags ->> 'access';
  v_designation text := p_tags ->> 'designation';
begin
  if v_highway in ('motorway', 'motorway_link') then
    return 'forbidden';
  end if;

  -- A bicycle tag decides: "dismount" means walking it, which is a Push, not riding.
  -- For customers only counts as private (T175).
  -- Except where riders have a public right (T179): a bridleway or byway through a golf club stays theirs.
  if coalesce(v_bicycle, v_access) = 'customers' and v_designation in ('public_bridleway', 'restricted_byway', 'byway_open_to_all_traffic') then
    return 'allowed';
  end if;
  if v_bicycle in ('no', 'private', 'dismount', 'customers') then
    return 'forbidden';
  end if;
  if v_bicycle in ('yes', 'designated', 'permissive') then
    return 'allowed';
  end if;

  if v_access in ('no', 'private', 'customers') then
    return 'forbidden';
  end if;

  -- A driveway is private unless its tags say otherwise (T081), as for a Run.
  if v_highway = 'service' and p_tags ->> 'service' = 'driveway' and coalesce(v_access, '') not in ('yes', 'permissive') then
    return 'forbidden';
  end if;

  -- A way built for bikes may be ridden, whatever right of way it was recorded as (5 km of the
  -- user's rides, 2026-11-04).
  if v_highway = 'cycleway' then
    return 'allowed';
  end if;

  -- A recorded right of way says who may use it, whatever the way looks like.
  if v_designation = 'public_footpath' then
    return 'forbidden';
  end if;
  if v_designation in ('public_bridleway', 'restricted_byway', 'byway_open_to_all_traffic') then
    return 'allowed';
  end if;

  if v_highway in ('footway', 'pedestrian', 'steps', 'corridor') then
    return 'forbidden';
  end if;
  -- A path with no say about bicycles may be a footpath or a shared path: the tags can't tell.
  if v_highway = 'path' then
    return 'unconfirmed';
  end if;
  return 'allowed';
end;
$$;
