-- 0048: Segment bearings (#149, T145c).
-- from_bearing, to_bearing: which way a Segment leaves each of its ends, in degrees clockwise from
--   grid north, along its own line a few metres in: what side of a Main Road it's on where it meets
--   one. Null on a Network built before, or where its line has no length to tell.
alter table network.segments add column from_bearing real, add column to_bearing real;
