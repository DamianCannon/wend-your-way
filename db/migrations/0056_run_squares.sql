-- 0056: Run hotspots (T190, #194; STORY-039): the off-road ground a Run may use, counted in 5 km squares of
-- Britain's grid, each by where its ways' middles lie. Every square holding any is kept, with its metres, its box
-- and a junction in it near its middle (by length) to mark it at; the app joins those holding enough that lie
-- side by side, into Run hotspots, so the line for a dense square can move without a build. About 6,500 for England and
-- Wales.
create table network.run_squares (
  id     serial                   primary key,
  metres real                     not null,
  box    geometry(Polygon, 27700) not null,
  at     geometry(Point, 27700)   not null,
  constraint ck_run_squares_has_metres check (metres > 0)
);
