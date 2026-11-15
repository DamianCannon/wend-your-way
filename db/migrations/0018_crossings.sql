-- 0018: Crossings (PLAN T044): a Segment of a crossing way, its kind, whether it crosses a Main
-- Road, and its share of the crossing (a crossing way cut into Segments shares its cost among
-- them by length). Set by build_segments(); none on any other Segment.
alter table network.segments
  add column crossing text constraint ck_segments_crossing check (crossing in ('unmarked', 'marked', 'signals')),
  add column crossing_main_road boolean not null default false,
  add column crossing_share real not null default 0 constraint ck_segments_crossing_share check (crossing_share between 0 and 1);
