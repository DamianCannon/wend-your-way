# User data refers to Segments by OSM identity, not by foreign key

The Network (`network` schema) is derived from public data and rebuilt wholesale, which renumbers every Segment `id`. The user's own data (`app` schema: Outings' matched Segments, Marks, Saved Loops) therefore refers to a Segment by its OSM identity, `(osm_way_id, from_node_id, to_node_id)`, and keeps a geometry snapshot. That identity survives a rebuild whenever the underlying OSM way is unchanged. When OSM does change a way, `reattach_user_data()` re-links the record to the closest matching Segment by geometry, or flags it as orphaned. This deliberately breaks the house rule that relationships are enforced by foreign keys.

## Considered options

- **Foreign keys from `app` into `network`.** A rebuild would either be blocked (`RESTRICT`) or would cascade-delete the user's Marks and Outing matches (`CASCADE`). Neither is acceptable.
- **Incremental Network updates that preserve ids.** This keeps foreign keys, but turns a simple "drop and rebuild" into diff-and-patch logic over tens of millions of rows. Not worth it for one user.

## Consequences

- Integrity between `app` and `network` is checked by `reattach_user_data()` after every rebuild, not by the database at write time.
- Every `app` table that points at Segments carries the three identity columns plus a geometry.
