-- 0041: beside the app's score, each scored route's share off-road and its climbing per km (PLAN
-- T104, STORY-045), from what was matched as the planner sees it: what the user likes that the
-- score alone misses. Null until scored again.
alter table app.outings
  add column off_road_share real check (off_road_share between 0 and 1),
  add column climb_per_km   real check (climb_per_km >= 0);
alter table app.saved_routes
  add column off_road_share real check (off_road_share between 0 and 1),
  add column climb_per_km   real check (climb_per_km >= 0);
