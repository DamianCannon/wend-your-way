-- 0025: Driving (PLAN T024, STORY-035): how long a car takes along each Segment it may use, each
-- way it may go, for drive times to Trailheads. From drive_speed_kph() and drive_oneway(), by the
-- build. Null one way where the Segment is one way the other.
create table network.segment_drive (
  segment_id  integer primary key,
  forward_s   real check (forward_s > 0),
  backward_s  real check (backward_s > 0),
  constraint ck_segment_drive_some_way check (forward_s is not null or backward_s is not null)
);
