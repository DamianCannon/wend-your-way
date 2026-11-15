-- 0038: the app's score for each Outing (PLAN T097, STORY-045), from which its stars are drawn:
-- its track matched onto the Network and scored as a Loop is. Both null until it's scored; a
-- matched_share under 0.8 or a score that isn't a number is "can't be rated". Kept, not worked out
-- on sight (a second each); cleared by a rebuild and scored again after it commits.
alter table app.outings
  add column score         real,
  add column matched_share real check (matched_share >= 0);
