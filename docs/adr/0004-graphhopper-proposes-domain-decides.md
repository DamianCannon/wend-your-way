---
status: superseded by ADR 0006
---

# GraphHopper proposes legs; the Domain decides which Loops are good

Generation has two layers. GraphHopper routes point-to-point legs between waypoints, using a per-request custom model built only from its built-in per-way values (road class, speed, surface, access, urban density, walking-route membership), plus polygon areas for Marks and Avoids. Our C# Domain maps every leg back to our own Segments, whose full attributes (Hazard, Obscurity, ground class, Opening Hours, Obstacles, Familiarity, same-ground group) come from PostGIS. It then computes the Breakdown and the authoritative score. GraphHopper's internal cost is never shown or trusted. This follows Trail Router's proven structure, keeps whole-Loop qualities (Flow, Retrace, distinctness, the Main Road rule) where they can be expressed and tested, and avoids writing a Java plugin.

## Considered options

- **Encode every preference into GraphHopper's weighting.** This needs a Java tag-parser plugin to get Obscurity, Hazard and Familiarity into the graph, and Flow, Retrace and distinctness still can't be expressed as per-edge costs. It remains the upgrade path if GraphHopper's approximation proves too crude: write our per-Segment values into the PBF as custom tags and add a small parser, behind the same `ILegRouter` port.

## Consequences

- GraphHopper can return a leg that our scorer dislikes. Generation compensates by asking for many candidates and steering with anchors (Corridor ends, unfamiliar Segments, Attractions) rather than relying on GraphHopper's costs.
- Every leg must be mapped back to Segments, so GraphHopper's results need OSM way identities (the `osm_way_id` path detail).
