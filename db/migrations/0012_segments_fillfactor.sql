-- 0012: room on each page of network.segments for the build's own updates (T018).
--
-- build_segments() stores Segments in map order, so a slice reads neighbouring pages. Then
-- group_same_ground() makes the pavements Connectors: a fifth of London's Segments. An updated
-- row moves to wherever there's room, which on full pages is the end of the table, scattering
-- them again. With a fifth of each page free, the update stays on the row's own page (ground isn't
-- indexed, so it's a heap-only update).
alter table network.segments set (fillfactor = 75);
