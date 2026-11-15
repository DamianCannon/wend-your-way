-- 0011: what an Outing was, apart from the Activity it informs (user, 2026-10-18).
--
-- The Strava archive holds runs, walks, hikes and rides. A walk or hike is on foot, so it informs
-- Run like a run does. Most of the user's rides are on the road: kept, because the user chose
-- their way, but informing neither Run nor Gravel Ride, so an Outing's Activity can be null.
-- Swims, rowing and virtual rides have no way on the ground and are never imported.
create type outing_kind as enum ('run', 'walk', 'hike', 'ride', 'gravel_ride');

alter table app.outings add column kind outing_kind;
update app.outings set kind = case activity when 'gravel_ride' then 'gravel_ride'::outing_kind else 'run'::outing_kind end;
alter table app.outings alter column kind set not null;

alter table app.outings alter column activity drop not null;
alter table app.outings add constraint ck_outings_kind_informs_activity check (
  activity is not distinct from case kind
    when 'run' then 'run'::activity when 'walk' then 'run'::activity when 'hike' then 'run'::activity
    when 'gravel_ride' then 'gravel_ride'::activity
    else null end);
