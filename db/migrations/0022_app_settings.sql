-- 0022: the app's own settings (PLAN T056): one row, its columns the settings. last_request is the
-- last Request's start, Distance and Leans (R1.1a, ARCHITECTURE), restored when the planner opens;
-- a start at home is kept as home, never as a position.
create table app.settings (
  id            boolean primary key default true,
  last_request  jsonb,
  constraint ck_settings_one_row check (id)
);
