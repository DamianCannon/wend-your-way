-- 0008: which road Segments each path Segment runs beside, and for how many metres (T014,
-- revisited after the Slice 1 review).
--
-- 0006 gave each path a single road Segment (same_ground_as): the one it runs beside longest. On
-- Greater London 881 km of paths were at least twice as long as that road Segment, so a pavement
-- spanning several of them shared ground with only one: Retrace was missed on the others, and
-- overcounted for the whole pavement against one short Segment. A path now lies on each road
-- Segment's ground for the metres it runs beside it, and on its own ground for the rest.
-- No foreign keys, as with same_ground_as: the build replaces every row at once.
create table network.segment_beside (
  path_id  integer          not null,
  road_id  integer          not null,
  metres   double precision not null,
  constraint pk_segment_beside primary key (path_id, road_id),
  constraint ck_segment_beside_has_metres check (metres > 0)
);

alter table network.segments drop column same_ground_as;
