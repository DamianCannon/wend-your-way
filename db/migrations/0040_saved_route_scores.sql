-- 0040: the app's score for a Saved Route (PLAN T097, STORY-045 AC2), kept when it's saved, with the
-- Activity it was planned for and the share of it on the map, from which its stars are drawn. Null
-- for a route saved before: it has no stars until it's saved again.
alter table app.saved_routes
  add column score         real,
  add column matched_share real check (matched_share >= 0),
  add column activity      activity;
