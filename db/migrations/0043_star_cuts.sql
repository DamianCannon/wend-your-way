-- 0043: the stars' cut points (PLAN T114), as `ct outings calibrate` last worked them out from the
-- user's Outings: {"run": [4 rising numbers], "ride": [4], "at": when, "runs": n, "rides": n}.
-- Null until then, when the cuts in code are used.
alter table app.settings add column star_cuts jsonb;
