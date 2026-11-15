-- 0035: Saved Routes (PLAN T080, STORY-015, R5.1): a Loop the user keeps by name. What the map
-- draws of it (its line, stops, Avoids, Breakdown, profile and ways) as the planner gave it, and its
-- Segments by OSM identity in travel order, to tell when the Network changes under it (STORY-016).
create table app.saved_routes (
  id         serial      primary key,
  name       text        not null,
  saved_at   timestamptz not null default now(),
  length_m   real        not null,
  for_map    jsonb       not null,
  traversals jsonb       not null,
  constraint ck_saved_routes_has_name check (length(btrim(name)) between 1 and 100)
);
