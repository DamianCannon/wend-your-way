-- 0015: Greenery (PLAN T036): the share of a Segment's length within 15 m of green areas, 0 to 1,
-- set by build_segments() as it inserts each Segment. Cuts its cost per metre.
alter table network.segments add column greenery real not null default 0
  constraint ck_segments_greenery check (greenery between 0 and 1);
