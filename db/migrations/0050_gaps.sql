-- 0050: short gaps in the map (#150, T146a, STORY-051).
-- gap: a Segment that is no way of its own but a gap between a path's dead end and another way it
--   stops short of, with nothing mapped across. 'joined': under 5 m, used like its path. 'possible':
--   5 m to 15 m, used only when the user includes it. Null for every other Segment. Its way id is its
--   path's, from its dead end; it ends at the other way's point there, or at a new point, numbered
--   minus the dead end's node id, where the other way is cut.
alter table network.segments add column gap text check (gap in ('joined', 'possible'));

-- How many the build made, so nothing is joined silently.
alter type segment_build
  add attribute gaps_joined    bigint,
  add attribute gaps_possible  bigint;
