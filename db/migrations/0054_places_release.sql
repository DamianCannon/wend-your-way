-- 0054: which release of OS Open Names the places are (T171, #175), written as they're loaded, in the same
-- transaction, so a map update knows whether OS has a newer one and the Admin panel can say which is in use.
-- In places, not app: a restore puts back app as it was, and would wind this back while the places stay.
-- One row, or none when they were loaded by hand.
create table places.release (
  -- OS's version, "2026-10".
  version   text not null,
  -- The MD5 of OS's zip they came from.
  md5       text not null,
  loaded_at timestamptz not null default now()
);
