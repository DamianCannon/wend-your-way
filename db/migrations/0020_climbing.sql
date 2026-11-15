-- 0020: Climbing (PLAN T049): the metres a Segment climbs and descends from its from node to its
-- to node, sampled from OS Terrain 50 by the Network build after Open ground.
alter table network.segments add column ascent_m real not null default 0;
alter table network.segments add column descent_m real not null default 0;
alter table network.segments add constraint ck_segments_climbing check (ascent_m >= 0 and descent_m >= 0);
