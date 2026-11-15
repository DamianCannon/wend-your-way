-- 0002: how an OSM way's tags become a ground class and Run access (PLAN T004).
-- Pure functions over the tags, so they can be tested one case at a time. They live in
-- public, like the enums, so they survive the network_next → network schema swap.
-- Rules follow CONTEXT.md and docs/spikes/2026-10-16-real-streets.md.

create or replace function classify_ground(p_tags jsonb)
returns ground_kind
language plpgsql
immutable
security invoker
set search_path = public, pg_temp
as $$
declare
  v_highway text := p_tags ->> 'highway';
  v_surface text := p_tags ->> 'surface';
  v_paved   boolean;
begin
  if v_highway is null then
    raise exception 'classify_ground: tags have no highway: %', p_tags
      using errcode = 'check_violation';
  end if;

  -- A pavement mapped as its own way runs beside traffic: a Connector (glossary).
  if p_tags ->> 'footway' = 'sidewalk' or p_tags ->> 'path' = 'sidewalk' then
    return 'connector';
  end if;

  -- Alleys are Traffic-free even when tagged as service roads (glossary: Traffic-free).
  if v_highway = 'service' and p_tags ->> 'service' = 'alley' then
    return 'traffic_free';
  end if;

  if v_highway in ('trunk', 'trunk_link', 'primary', 'primary_link', 'secondary', 'secondary_link',
                   'tertiary', 'tertiary_link', 'unclassified', 'residential', 'living_street',
                   'service', 'road') then
    return 'connector';
  end if;

  v_paved := case
    when v_surface in ('unpaved', 'dirt', 'earth', 'ground', 'grass', 'gravel', 'fine_gravel',
                       'compacted', 'mud', 'woodchips', 'sand', 'pebblestone', 'grass_paver') then false
    when v_surface is not null then true
    -- No surface tag: paths, tracks and bridleways are usually unsurfaced, footways,
    -- pedestrian ways, cycleways and steps usually paved. Greenery will refine this.
    else v_highway not in ('path', 'track', 'bridleway')
  end;

  return case when v_paved then 'traffic_free'::ground_kind else 'off_road'::ground_kind end;
end;
$$;

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
  return 'allowed';
end;
$$;
