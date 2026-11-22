-- 0052: Main Roads with a real pavement (#185, T181, STORY-002).
-- pavement: a Main Road 30 mph or under with pavements on both sides (has_pavement), or a pavement mapped
--   as its own way beside such a road. A Run pays less a metre along it. False everywhere else, and on
--   a Network built before T181 until the next build.
alter table network.segments add column pavement boolean not null default false;
