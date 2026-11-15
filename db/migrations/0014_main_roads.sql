-- 0014: Main Roads (R2.1, CONTEXT.md): trunk, primary and secondary roads and their links, and
-- the pavements beside them. A Loop uses one only for a short link (user, 2026-10-19). Set by
-- build_segments() from the road's highway tag and by group_same_ground() for pavements; adding
-- the column with a constant default doesn't rewrite the table.
alter table network.segments add column main_road boolean not null default false;
