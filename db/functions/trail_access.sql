-- Whether a Trail Ride may use a Segment (PLAN T021; SPEC R9.6; CONTEXT.md: Access, Unconfirmed
-- Access). Off the roads, only a highway authority's Definitive Map byway (p_on_byway: the build's
-- match, 70% of the Segment within 20 m of one), unless OSM says it is less; an unclassified green
-- lane, or OSM's byway where no Definitive Map is open (p_covered false), is Unconfirmed: used,
-- flagged "check this". Roads may be ridden, A-roads too (AC4), but not motorways. Bans and
-- closures, seasonal or not, are months (motor_closed_months), left out of a slice by its date; a
-- closure that can't be read, off the roads, is Unconfirmed.
-- Repeatable: Migrations applies every script in db/functions after the numbered migrations, on
-- every run, so a function is changed by editing it here (create or replace).

create or replace function trail_access(p_tags jsonb, p_on_byway boolean, p_covered boolean)
returns access_kind
language plpgsql
immutable
security invoker
set search_path = public, pg_temp
as $$
declare
  v_highway     text := p_tags ->> 'highway';
  v_designation text := coalesce(p_tags ->> 'designation', '');
  v_unread      boolean := motor_closure_unread(p_tags);
begin
  if v_highway in ('motorway', 'motorway_link') then
    return 'forbidden';
  end if;

  -- What OSM records as a lesser right of way stays one, whatever line it's near.
  if v_designation ~ '(footpath|bridleway|restricted_byway)' then
    return 'forbidden';
  end if;
  if v_highway in ('footway', 'cycleway', 'steps', 'pedestrian', 'corridor', 'elevator', 'platform', 'bus_guideway', 'escape', 'raceway') then
    return 'forbidden';
  end if;

  -- For customers only counts as private (T175): by the motor tag that applies, most specific first.
  if coalesce(p_tags ->> 'motorcycle', p_tags ->> 'motor_vehicle', p_tags ->> 'vehicle', p_tags ->> 'access', '') = 'customers' then
    return 'forbidden';
  end if;

  -- A driveway is private unless its tags say otherwise, as for a Run.
  if v_highway = 'service' and p_tags ->> 'service' = 'driveway'
     and coalesce(p_tags ->> 'motorcycle', p_tags ->> 'motor_vehicle', p_tags ->> 'access', '') not in ('yes', 'permissive', 'designated') then
    return 'forbidden';
  end if;

  if p_on_byway then
    return case when v_unread then 'unconfirmed' else 'allowed' end;
  end if;
  if v_designation ~ '^(byway_open_to_all_traffic|public_byway)' then
    return case when p_covered then 'forbidden' else 'unconfirmed' end;
  end if;
  if is_motor_road(p_tags) then
    return 'allowed';
  end if;
  if v_designation like 'unclassified_highway%' then
    return 'unconfirmed';
  end if;
  return 'forbidden';
end;
$$;
