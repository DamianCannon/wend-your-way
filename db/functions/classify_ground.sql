-- How an OSM way's tags become a ground class (PLAN T004; CONTEXT.md: Off-road, Traffic-free, Connector).
-- Repeatable: Migrations applies every script in db/functions after the numbered migrations, on
-- every run, so a function is changed by editing it here (create or replace). Schema changes
-- still go in numbered migrations.

-- Whether a way's surface tag says it's unpaved: the one list classify_ground() and same ground
-- (group_same_ground) both read (T038).
create or replace function tagged_unpaved(p_tags jsonb)
returns boolean
language sql
immutable
as $$
  select coalesce(p_tags ->> 'surface' in ('unpaved', 'dirt', 'earth', 'ground', 'grass', 'gravel', 'fine_gravel',
                                           'compacted', 'mud', 'woodchips', 'sand', 'pebblestone', 'grass_paver'), false)
$$;

-- A pure function over the tags, so it can be tested one case at a time. It lives in public,
-- like the enums, so it survives the network_next → network schema swap. Rules follow
-- CONTEXT.md and docs/spikes/2026-10-16-real-streets.md.
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
  -- Closed to motor vehicles, with no buses either. Missing tags count as '' so no test is null.
  v_no_motor_traffic boolean :=
    (coalesce(p_tags ->> 'motor_vehicle', '') = 'no' or coalesce(p_tags ->> 'vehicle', '') = 'no'
     or (coalesce(p_tags ->> 'access', '') = 'no' and not p_tags ? 'motor_vehicle'))
    and coalesce(p_tags ->> 'bus', p_tags ->> 'psv', '') not in ('yes', 'designated');
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

  -- A road carries traffic, unless it's closed to motor vehicles (a park drive, a road for bikes
  -- only) and no buses run on it; then it's ground like any path. access=private still carries
  -- residents' cars, so it stays a Connector.
  if v_highway in ('motorway', 'motorway_link', 'trunk', 'trunk_link', 'primary', 'primary_link', 'secondary', 'secondary_link',
                   'tertiary', 'tertiary_link', 'unclassified', 'residential', 'living_street',
                   'service', 'road')
     and not v_no_motor_traffic then
    return 'connector';
  end if;

  v_paved := case
    when tagged_unpaved(p_tags) then false
    when v_surface is not null then true
    -- No surface tag: paths, tracks, bridleways and byways are usually unsurfaced, footways,
    -- pedestrian ways, cycleways and steps usually paved, except a public footpath mapped as a
    -- footway: mostly across fields (OS said unmade for 20.1 of 24.4 km, T174). Beside a road,
    -- only a tag will do (group_same_ground, T038).
    when v_highway = 'footway' and p_tags ->> 'designation' = 'public_footpath' then false
    else v_highway not in ('path', 'track', 'bridleway', 'byway')
  end;

  return case when v_paved then 'traffic_free'::ground_kind else 'off_road'::ground_kind end;
end;
$$;

-- A road with a real pavement to walk on (T181): 30 mph or under (48 km/h), with pavements on both
-- sides, tagged on the road or mapped as ways of their own. Where a Main Road costs a Run less.
create or replace function has_pavement(p_tags jsonb)
returns boolean
language sql
immutable
as $$
  select coalesce(coalesce(
           case when p_tags ->> 'maxspeed' ~ '^\d+ mph$' then split_part(p_tags ->> 'maxspeed', ' ', 1)::int <= 30
                when p_tags ->> 'maxspeed' ~ '^\d+$' then (p_tags ->> 'maxspeed')::int <= 48
                else false end, false)
     and (p_tags ->> 'sidewalk' in ('both', 'separate')
          or p_tags ->> 'sidewalk:both' in ('yes', 'separate')
          or (p_tags ->> 'sidewalk:left' in ('yes', 'separate') and p_tags ->> 'sidewalk:right' in ('yes', 'separate'))), false)
$$;
