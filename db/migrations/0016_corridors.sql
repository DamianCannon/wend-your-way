-- 0016: Corridors (PLAN T037): the name of the walking route, or national or international cycle
-- route, that a green Segment lies on, or null. Set by build_segments() as it inserts each Segment.
-- Cuts its cost per metre.
alter table network.segments add column corridor text;
