-- 0021: Segments stored in order of place (PLAN T055). A slice is read through the spatial index,
-- but the rows it finds were scattered over the table: 40 km from Kenwood House read 1,068,400
-- Segments from 213,000 of Great Britain's pages. GiST can't order a table, so this B-tree on each
-- Segment's start point does: PostGIS sorts points along a Hilbert curve, and the Network build
-- clusters the new Network's Segments on it before the swap.
-- If not exists: a database clustered by hand before this ran keeps its index.
create index if not exists ix_segments_place on network.segments using btree (ST_StartPoint(geom));
