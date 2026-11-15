-- 0029: no drive times (PLAN T065): Find a start went from the page (T063), so nothing reads how
-- long a car takes along a Segment or the roads a car reaches a Trailhead by ("1 - remove", user,
-- 2026-11-03).
drop table if exists network.segment_drive;
alter table network.trailheads drop column if exists drive_nodes;
drop function if exists drive_speed_kph(jsonb);
drop function if exists drive_oneway(jsonb);
