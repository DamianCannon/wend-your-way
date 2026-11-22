-- 0051: the map services' API keys, kept here and managed in the Admin panel (#173, T169).
-- name: the setting it stands for, as in configuration ('Thunderforest:ApiKey', 'OsMaps:ApiKey',
--   'OsMaps:Premium'); a saved one wins over configuration's. Never backed up, and left as it is by
--   a restore (UserDataBackups.NotTheUsers).
create table app.api_keys (
    name       text primary key,
    value      text not null,
    changed_at timestamptz not null default now()
);
