-- 0039: the user's own stars, one to five (PLAN T097, STORY-045 AC1), on an Outing and on a Saved
-- Route, beside the app's: null until given. The user's data: a rebuild never touches them.
alter table app.outings
  add column user_stars smallint check (user_stars between 1 and 5);
alter table app.saved_routes
  add column user_stars smallint check (user_stars between 1 and 5);
