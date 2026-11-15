-- 0030: what's known of a Trailhead beyond where it is (PLAN T066), kept by the build from its
-- tags and what's within 300 m: only what the map says, never a guess.
alter table network.trailheads add column fee text check (fee in ('Free', 'Donation', 'Pay'));
alter table network.trailheads add column capacity integer check (capacity >= 0);
-- Its own opening_hours as tagged, unread: few car parks have one.
alter table network.trailheads add column hours text;
alter table network.trailheads add column toilets boolean not null default false;
alter table network.trailheads add column cafe boolean not null default false;
alter table network.trailheads add column pub boolean not null default false;
