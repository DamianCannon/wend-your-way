-- 0053: ways for customers only (#186, T182, STORY-002).
-- customers: tagged for customers only (has_customers): used, but costed a little more and drawn striped
--   on a Loop, as OSM's tagging of it is too often wrong to rule it out. False on a Network built before
--   T182 until the next build.
alter table network.segments add column customers boolean not null default false;
