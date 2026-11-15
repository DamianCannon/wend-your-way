---
status: accepted
---

# Route in-process over Network slices; no GraphHopper

Loops are routed by our own A* search in C#, in memory, over the part of the Network a Request needs (a "slice" loaded from PostGIS around the start). The edge cost is computed from each Segment's attributes using the same `ScoringWeights` that score whole Loops, plus per-request adjustments (Marks, Avoids, Opening Hours at the expected time, an unfamiliarity discount set by the Novelty Lean, Lean multipliers, a penalty on same-ground groups already used by the Loop). Trail Router needs a separate engine because it routes the whole planet for thousands of users a day; one user's Loops in one area are a few hundred thousand Segments at most, and A* over that takes milliseconds per Path.

This supersedes ADR 0004 and the GraphHopper parts of ADR 0003.

## Considered options

- **Local GraphHopper (the previous decision).** It's proven and comes with map matching, but it only approximates our preferences through its built-in tags, needs Java and a second graph built from the same PBF (the 8–10 GB peak on a 16 GB laptop), and every route has to be mapped back onto our Segments. It stays possible behind the `IPathFinder` seam if our router disappoints.

## Consequences

- Routing and scoring share one cost function, so generation optimises exactly what the Breakdown measures, and Way Choice calibration (version 1) can fit the same weights directly.
- The router is unit-tested on tiny hand-built graphs; no Java process in tests or on the laptop.
- We own routing correctness: the Network's topology (junctions, bridges, tunnels) and `derive_access()` must be right, because nothing else checks them.
- GPS map matching is no longer free. The first cut uses buffer coverage over same-ground groups instead; sequence matching for Deviations and Way Choices is designed in version 1.
- If loading a large slice (a long Gravel Ride) proves slow, the fix is a compact routing-graph file written by `wend network build`, not a routing engine.

## Note, 2026-10-19: Flow is a whole-Loop term

*Amended 2026-10-23 (PLAN T042):* the score no longer charges exactly what the router does. Checked against the user's verdicts on pairs of Loops from the same start, the router's charges punished whole Loops for what the user accepts: a Main Road link to reach a park at five times a road, and out and back along a canal at four times the ground. Judging a whole Loop, the score costs a Main Road as an ordinary road and reused ground at twice its rate, and takes 15% off for each Knot; the router keeps its steeper charges, which steer each Path. Generation still chooses by the score.

Flow (PLAN T033) can't be an edge cost: whether a green Segment is in a long stretch depends on the Segments either side of it in the finished Loop. So the scorer multiplies a Loop's per-km score by (1 + FlowWeight × its share in long green stretches), and the router's edge costs stay as they were. Generation still optimises what the score measures for every edge-level term; for Flow it only chooses between candidates, and A* doesn't steer towards long stretches. If the user's Loops show that matters, Corridors (increment 1) are the way to steer: Anchors placed on long green chains.
