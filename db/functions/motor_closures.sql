-- When OSM closes a way to a trail bike (PLAN T021; SPEC R9.6): the months it is closed, as bits
-- (January the lowest, 4095 all year), and whether a closing rule couldn't be read. From its ban,
-- then its conditional tags, the more particular last, then its traffic_regulation_order: a closing
-- rule in months closes those months, whole; an opening rule opens them; a rule for more than two
-- wheels, or for someone else (horses, bicycles, permit holders), is passed over. A closing rule
-- it can't read (hours, snow, "when barrier is locked") closes nothing: off the roads the way says
-- "check this" (trail_access); on a road, a school street's hours are passed over.
-- Repeatable: Migrations applies every script in db/functions after the numbered migrations, on
-- every run, so a function is changed by editing it here (create or replace).

-- A value's rules, split at the semicolons between them, not those inside brackets.
create or replace function motor_rules_of(p_text text)
returns text[]
language plpgsql
immutable
security invoker
set search_path = public, pg_temp
as $$
declare
  v_rules text[] := '{}';
  v_rule  text := '';
  v_depth integer := 0;
  v_char  text;
begin
  foreach v_char in array regexp_split_to_array(coalesce(p_text, ''), '') loop
    if v_char = '(' then v_depth := v_depth + 1;
    elsif v_char = ')' then v_depth := greatest(v_depth - 1, 0);
    end if;
    if v_char = ';' and v_depth = 0 then
      v_rules := v_rules || v_rule;
      v_rule := '';
    else
      v_rule := v_rule || v_char;
    end if;
  end loop;
  return v_rules || v_rule;
end;
$$;

-- The months of a range such as "Oct-Apr", "1 Oct - 30 Apr", "Oct 01-Apr 30" or "1 November-31
-- March", as bits; null if the words are anything else.
create or replace function motor_months(p_words text)
returns integer
language plpgsql
immutable
security invoker
set search_path = public, pg_temp
as $$
declare
  c_month constant text := '(jan|feb|mar|apr|may|jun|jul|aug|sep|oct|nov|dec)[a-z]*\.?';
  v_match text[];
  v_from  integer;
  v_to    integer;
  v_bits  integer := 0;
  v_month integer;
begin
  v_match := regexp_match(btrim(p_words),
    '^(?:\d{1,2}\s*)?' || c_month || '(?:\s*\d{1,2})?\s*-\s*(?:\d{1,2}\s*)?' || c_month || '(?:\s*\d{1,2})?$');
  if v_match is null then
    return null;
  end if;
  v_from := (position(v_match[1] in 'janfebmaraprmayjunjulaugsepoctnovdec') + 2) / 3;
  v_to := (position(v_match[2] in 'janfebmaraprmayjunjulaugsepoctnovdec') + 2) / 3;
  v_month := v_from;
  loop
    v_bits := v_bits | (1 << (v_month - 1));
    exit when v_month = v_to;
    v_month := v_month % 12 + 1;
  end loop;
  return v_bits;
end;
$$;

-- One tag's rules applied to the months so far. p_subjects: null for a conditional tag, whose
-- rules are all about its own key; for a traffic_regulation_order, the subjects this pass is for
-- (a rule naming none is for everyone, read with vehicles).
create or replace function motor_rules(p_text text, p_subjects text[], p_implicit boolean, inout months integer, inout unread boolean)
language plpgsql
immutable
security invoker
set search_path = public, pg_temp
as $$
declare
  c_closing  constant text[] := array['no', 'private', 'permit', 'agricultural', 'forestry', 'destination', 'delivery'];
  c_opening  constant text[] := array['yes', 'designated', 'permissive'];
  c_subjects constant text[] := array['vehicle', 'motor_vehicle', 'motorcycle', 'motorcar', 'hgv', 'goods', 'psv', 'bus', 'moped', 'mofa',
                                      'bicycle', 'horse', 'foot', 'carriage', 'horse-drawn_vehicle', 'horse-drawn_vehicles'];
  v_rule    text;
  v_value   text;
  v_cond    text;
  v_part    text;
  v_word    text;
  v_named   boolean;
  v_ours    boolean;
  v_wheels  boolean;
  v_read    boolean;
  v_months  integer;
  v_range   integer;
begin
  if p_text is null then
    return;
  end if;
  foreach v_rule in array motor_rules_of(lower(p_text)) loop
    continue when position('@' in v_rule) = 0;
    v_value := btrim(split_part(v_rule, '@', 1));
    continue when not (v_value = any (c_closing) or v_value = any (c_opening));
    v_cond := btrim(translate(substr(v_rule, position('@' in v_rule) + 1), '()"', ''));

    -- Either side of an "or" is a condition of its own; so is each of a list in brackets.
    foreach v_part in array regexp_split_to_array(v_cond, '\s*(\|\||;|\mor\M)\s*') loop
      v_named := false;
      v_ours := false;
      v_wheels := false;
      v_read := true;
      v_months := null;
      foreach v_word in array regexp_split_to_array(btrim(v_part), '\s+and\s+') loop
        v_word := btrim(v_word);
        if v_word ~ '^wheels\s*>\s*2$' then
          v_wheels := true;
        elsif p_subjects is not null and v_word = any (c_subjects) then
          v_named := true;
          v_ours := v_ours or v_word = any (p_subjects);
        else
          v_range := motor_months(v_word);
          if v_range is null then
            v_read := false;
          else
            v_months := coalesce(v_months, 0) | v_range;
          end if;
        end if;
      end loop;

      continue when v_wheels;
      continue when p_subjects is not null and not (v_ours or (not v_named and p_implicit));
      if v_value = any (c_closing) then
        if v_read then
          months := months | coalesce(v_months, 4095);
        else
          -- Months with something more ("Oct-Apr when the barrier is locked"): closed then, at least.
          months := months | coalesce(v_months, 0);
          unread := true;
        end if;
      elsif v_read then
        months := months & ~coalesce(v_months, 4095);
      end if;
    end loop;
  end loop;
end;
$$;

create or replace function motor_closures(p_tags jsonb, out months integer, out unread boolean)
language plpgsql
immutable
security invoker
set search_path = public, pg_temp
as $$
declare
  v_ban text := coalesce(p_tags ->> 'motorcycle', p_tags ->> 'motor_vehicle', p_tags ->> 'vehicle', p_tags ->> 'access');
  v_key text;
begin
  months := case when v_ban in ('no', 'private', 'permit', 'agricultural', 'forestry', 'destination', 'delivery') then 4095 else 0 end;
  unread := false;
  -- Most ways have no rules at all: the build asks of every one.
  if not p_tags ?| array['access:conditional', 'vehicle:conditional', 'motor_vehicle:conditional', 'motorcycle:conditional', 'traffic_regulation_order'] then
    return;
  end if;
  foreach v_key in array array['access:conditional', 'vehicle:conditional', 'motor_vehicle:conditional'] loop
    select r.months, r.unread into months, unread from motor_rules(p_tags ->> v_key, null, false, months, unread) r;
  end loop;
  select r.months, r.unread into months, unread
    from motor_rules(p_tags ->> 'traffic_regulation_order', array['vehicle', 'motor_vehicle'], true, months, unread) r;
  select r.months, r.unread into months, unread from motor_rules(p_tags ->> 'motorcycle:conditional', null, false, months, unread) r;
  select r.months, r.unread into months, unread
    from motor_rules(p_tags ->> 'traffic_regulation_order', array['motorcycle'], false, months, unread) r;
  -- On a road, a closing rule it can't read is a school street's or a loading bay's hours.
  if is_motor_road(p_tags) then
    unread := false;
  end if;
end;
$$;

-- A road for traffic, motorways too: where a trail bike rides as any motor does.
create or replace function is_motor_road(p_tags jsonb)
returns boolean
language sql
immutable
security invoker
set search_path = public, pg_temp
as $$
  select coalesce(p_tags ->> 'highway' in ('motorway', 'motorway_link', 'trunk', 'trunk_link', 'primary', 'primary_link',
                                           'secondary', 'secondary_link', 'tertiary', 'tertiary_link', 'unclassified',
                                           'residential', 'living_street', 'service', 'road'), false);
$$;

create or replace function motor_closed_months(p_tags jsonb)
returns integer
language sql
immutable
security invoker
set search_path = public, pg_temp
as $$
  select (motor_closures(p_tags)).months;
$$;

create or replace function motor_closure_unread(p_tags jsonb)
returns boolean
language sql
immutable
security invoker
set search_path = public, pg_temp
as $$
  select (motor_closures(p_tags)).unread;
$$;
