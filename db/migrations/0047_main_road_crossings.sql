-- 0047: Main Road crossings (#149, T145a).
-- busy: whether a Segment's road is busy (a trunk road, four lanes or more, or one carriageway of a
--   dual carriageway), where crossing with no crossing point is never chosen.
-- carriageway: whether it is a Main Road's carriageway itself, not a pavement beside one (which is a
--   Main Road too, for its traffic): the carriageway's sides are what's crossed.
-- Both null on a Network built before, which is planned on as it was until it's rebuilt.
-- crossing_nodes: the crossing points mapped on Main Roads, by OSM node, each a junction of its road.
alter table network.segments add column busy boolean, add column carriageway boolean;

create table network.crossing_nodes (
  node_id bigint primary key,
  kind    text   not null constraint ck_crossing_nodes_kind check (kind in ('unmarked', 'marked', 'signals'))
);
