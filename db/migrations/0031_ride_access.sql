-- 0031: whether a Gravel Ride may use each Segment (PLAN T068), from ride_access(tags), kept by
-- the build. Empty on a Network built before Gravel Rides: filling it here would rewrite every
-- Segment as the app starts, so a Gravel Ride asks for `ct network rebuild` instead.
alter table network.segments add column access_ride access_kind;
