-- 0017: Waterside (PLAN T043): the share of a Segment's length within 25 m of water, 0 to 1, set
-- by build_segments() as it inserts each Segment. Raises a Loop's score; the router already
-- treats water as green through Greenery.
alter table network.segments add column waterside real not null default 0
  constraint ck_segments_waterside check (waterside between 0 and 1);
