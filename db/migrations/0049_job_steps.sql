-- 0049: which step a map update is on, of how many (#152, T148): "step 12 of 17, working out the climbing",
-- for the job line. The stage stays as it was, for what the next update reads of the last.
alter table app.map_updates add column step text;
