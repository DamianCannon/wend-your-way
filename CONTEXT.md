# Wend Your Way

The shared language of the project: generate running and cycling loops that stay
off main roads by stitching together footpaths, bridleways, alleyways, cut-throughs
and other rights of way, and that join them up safely. England and Wales only;
Scotland's access law is out of scope. This file is a glossary only, with no
implementation detail.

## Asking for and shaping a Loop

**Loop**:
A generated route that starts and finishes at the same place, built to a
requested Distance and Activity. The thing the user asks for and chooses between.
_Avoid_: route, circuit, run, ride.

**Point-to-point**:
A generated route from one place to another, with the same Activity, Breakdown
and scoring as a Loop: built to a requested Distance at least as long as the
shortest way, so it can wander onto better ground; or, with no Distance, offered
as a spread from near the shortest way to the greenest (user, 2026-11-08). Any
line ending over 200 m from where it began is one, opened or imported too; its
GPX calls it a route.
_Avoid_: A-to-B, one-way, journey.

**Place search**:
Finding a start or end by typing a postcode, place, road or address instead of
clicking the map. Answered on the laptop from open data, so a search never
leaves it.
_Avoid_: geocoding, lookup, address bar.

**Activity**:
What a Loop is generated for: **Run**, **Gravel Ride** or **Trail Ride**. Every
Loop has exactly one Activity, and the Activity decides which ways the Loop may use.
_Avoid_: mode, sport, profile.

**Trail Ride**:
An Activity on a road-legal motorcycle: byways where motor vehicles have a recorded
right, joined up by roads of any kind but motorways. A byway is one on a highway
authority's **Definitive Map** (open data via rowmaps.com); a way OSM closes to motors
is left out, a seasonal closure in its months. Much longer than a Run or Gravel Ride,
and rare in London. Every Trail Ride says to check for closures before riding.
_Avoid_: green laning, enduro, moto, motorbike ride.

**Request**:
What the user asks the generator for: a start point, an Activity, a target
Distance, a start time (now unless given, so Opening Hours can be judged), its
Leans and Heading, and optionally one or more Vias. Answered with up to five
Loops, best first, that differ genuinely from one another, not near-copies.
The last Request's settings are remembered for the next.
_Avoid_: query, search, plan.

**Distance**:
The target length of a Loop. Loops within about a kilometre either side of it
(11–13 km for 12 km) satisfy the Request.
_Avoid_: length, target, range.

**Lean**:
A per-Request push towards or away from one quality: **Greenery**,
**Off-road**, **Hills** or **Novelty**. Its centre is the calibrated weight;
moving it changes that Request only, within limits, and never changes the
weights themselves.
_Avoid_: slider, weight, preference, setting.

**Novelty**:
The Lean towards unfamiliar ground. At zero, Familiarity is ignored; the higher
it is, the more strongly Loops are steered onto ground no Outing has covered.
_Avoid_: surprise, exploration, discovery.

**Heading**:
The compass direction a Request would like its Loops to head towards, and how
strongly. With no Heading, Loops may go any way.
_Avoid_: direction, bearing, bias, orientation.

**Anchor**:
A point the generator chooses for a Loop to pass through: the end of a
Corridor, a stretch of unfamiliar ground, an Attraction. A Loop runs from its
start through its Anchors (and any Vias) and back, and they are shown numbered
along it. Unlike a Via, the user never sets one.
_Avoid_: waypoint, pivot, control point.

**Destination**:
A large Off-road network a Loop goes to and makes the most of: round its edge,
or up one side and back down the other, as Trail Router's best Loops do in
Richmond Park or the Lea Valley. A Loop goes to one or two (user, 2026-10-23),
through its gates: the junctions where ways lead out of it.
_Avoid_: target, hotspot.

**Knot**:
A junction a Loop comes back to other than while retracing its own steps: a
spur out to touch somewhere, a lollipop, a tangle. What the user means by a
route "pulled all over the place". Out and back the same way is not a Knot.
_Avoid_: loop-back, self-intersection.

**Via**:
A point or Segment a Loop must pass through, given either in the Request or
afterwards by clicking on the map ("go this way").
_Avoid_: waypoint, pin, must-pass.

**Avoid**:
A Segment the user excludes from one Loop only, such as a street past a school at
home time. Unlike a Mark it says nothing about the Segment next time.
_Avoid_: exclude, block (a Blocked Mark is permanent).

**Nudge**:
Changing a Loop by adding a Via or an Avoid. The generator repairs only the part
of the Loop around the change and keeps the rest; the user never draws the line
by hand.
_Avoid_: edit, drag, reroute.

**Saved Route**:
A Loop or Point-to-point the user has chosen to
keep by name, so it can be run again and exported; listed under Saved Routes.
Offered Loops that are not saved are thrown away. Was "Saved Loop" until the
user named the list (2026-11-07: "the label on that should say Saved Routes as I
could have point to point routes too").
_Avoid_: favourite, library entry, Saved Loop.

**Breakdown**:
The explanation shown with every offered Loop of why it scored as it did:
Off-road share, longest unbroken stretch, Crossings, Connectors, Obstacles, new
ground, climbing. Scoring is fixed rather than adjustable by the user, so the
Breakdown is what earns their trust.
_Avoid_: score, stats, summary.

**Reference Loop**:
A Loop the user has personally judged, kept with their verdict in the
git-ignored `data/reference-loops/`, used to check the scoring: however the
weights change, every Loop they called very good must keep outscoring every one
they called good. The user's stars on Outings and Saved Routes now check the
scoring the same way, at scale (T108, T109).
_Avoid_: test route, benchmark, golden route.

## The ground a Loop covers

**Segment**:
The stretch of a single way between two junctions, where a junction is any point
a Loop could choose to go another way. A Loop is a chain of Segments; a Segment is
the smallest unit the user can have an opinion about.
_Avoid_: section, leg, link (and Strava's meaning of segment: a named, timed
stretch).

**Off-road**:
Unsurfaced ground free of motor traffic: field-edge footpaths, bridleways, tracks,
woodland trails. The ground a Loop tries to maximise.
_Avoid_: trail (too vague), cross-country, unpaved.

**Traffic-free**:
Hard-surfaced ground with no motor traffic, such as alleys, cut-throughs and
shared-use paths. Better than any Connector, worse than Off-road.
_Avoid_: path, pavement, car-free.

**Open ground**:
An area the user can cross freely in any direction, with no path drawn across it:
playing fields, open grass in a park, commons, heath, meadow. Crossed by straight
Open ground Segments between the places where ways meet its edge, staying inside
it; they count as Off-road.
_Avoid_: open space, open access land (a legal designation under the CROW Act,
narrower than this).

**Flow**:
How rarely a Loop is interrupted. Long unbroken Off-road or Traffic-free
stretches (a canal towpath, a disused railway line turned path) score far more
than the same distance chopped into scraps. Every Crossing and every switch onto
a Connector breaks Flow, and coming back over ground already covered doesn't
lengthen a stretch.
_Avoid_: continuity, rhythm, smoothness.

**Corridor**:
A long, continuous stretch of Off-road or Traffic-free ground that a Loop can be
built around: a towpath, a disused railway line, a named walking route such as
the Capital Ring or the Thames Path. Where good Flow comes from.
_Avoid_: trail, greenway, route.

**Greenery**:
How much of a way's surroundings is park, wood, common, water or open country.
It separates a paved path across a heath from a paved path beside a bypass,
which surface and traffic alone cannot.
_Avoid_: green index, nature, scenery.

**Obscurity**:
How hidden and unlikely a way is: the narrow alley or forgotten cut-through that
an ordinary router would never pick. A bonus that separates a good Traffic-free
alley from a dull roadside cycle path.
_Avoid_: quirkiness, interest, novelty.

**Obstacle**:
Something on a Segment that slows or interrupts an Activity without making the
way unlawful: gates, stiles, kissing gates, anti-motorbike barriers, cattle
grids, fords. Its cost depends on the Activity (nothing on a Run, far more on a
Gravel Ride), but it is never forbidden. It is worth paying when it opens up good
Flow. Steps are not an Obstacle on a Gravel Ride but forbidden: a bike carried up
them would be a Push (user, 2026-11-05).
_Avoid_: barrier (only one kind), impediment, blocker.

**Attraction**:
Anything along the way that enriches a Loop: a view, a watermill, a hillfort, a
café. A mild bonus and a reason to take a Spur, never the point of the Loop. A
stretch with a wide open view counts as well as a single place.
_Avoid_: point of interest, POI, landmark, highlight.

## Roads

**Connector**:
A stretch of a Loop that carries motor traffic or runs right beside it, used
only to get from one traffic-free way to the next. Tolerated, never sought. A
pavement is a Connector even when it is mapped as a path of its own: it is not
where anyone would choose to run, though its Hazard is lower than the road's.
_Avoid_: link, road section, gap.

**Hazard**:
How dangerous a Connector is per metre to be on, driven by traffic speed, traffic
volume, and whether there is a pavement or verge to step onto. A quiet
residential street linking two good trails has almost none.
_Avoid_: risk, danger score, safety.

**Exposure**:
Hazard accumulated over the length of a Connector. A very hazardous road is
acceptable for a short enough stretch; what the user balances is Exposure, not
Hazard alone.
_Avoid_: total risk, cost.

**Main Road**:
A Connector with heavy traffic. Costly enough that a Loop uses one only for a
short link between better ground, and never merely because it's beside green.
_Avoid_: busy road, A-road, arterial.

**Crossing**:
A point where a Loop crosses a road rather than travelling along it. Scored
separately from Connectors and always shown to the user. Footbridges, underpasses
and signalled crossings are Crossings with almost no Hazard, which is why a detour
to one is usually worth it.
_Avoid_: junction, road crossing point.

## Shape

**Retrace**:
Covering the same ground twice within one Loop. Penalised by length rather than
forbidden, at the ground's own rate whatever made it cheap the first time. Going back along the other pavement of the same road, or along a path
a few metres from the first, is still the same ground. A Loop that crosses its
own line without repeating any ground (a figure-of-eight) is not a Retrace.
_Avoid_: out-and-back, backtrack, doubling.

**Same ground**:
Ground that two Segments cover between them: a road and the pavements or tracks
beside it. A Segment lies on another's ground only for the metres it runs beside
it, so a long pavement can share a little of several road Segments' ground and
keep the rest as its own. How Retrace and Familiarity are measured.
_Avoid_: duplicate, parallel way.

**Stem**:
A Retrace that starts at the Loop's start point: leaving and returning home the
same way. Penalised more gently than any other Retrace, because it is often
unavoidable.
_Avoid_: lollipop stick, lead-in, approach.

**Spur**:
A Retrace in the middle of a Loop, going out and back to reach an Attraction. A
Loop has at most one Spur, and a Spur that reaches nothing is never taken.
_Avoid_: detour, dead-end, branch.

## Who may go where

**Access**:
Whether an Activity may lawfully use a way at the time it would be there. This
covers public rights of way and permissive ground alike: parks, commons,
towpaths, cemeteries, estate walkways. A Run may use any of it; a Gravel Ride may
not use public footpaths (other than a Push) or ways where cycling is banned. A
Trail Ride may use only roads and ways with a recorded motor vehicle right (a
byway open to all traffic, or a road the Network knows to be public), and never
one closed by a Traffic Regulation Order; where it can't tell, the way is not used.
_Avoid_: permission, allowed ways, right of way (only one kind of Access).

**Unconfirmed Access**:
A way where the Network cannot tell whether an Activity may use it, typically a
park path on a Gravel Ride where there is no Definitive Map, or on a Trail Ride a
green lane (an unclassified road off the tarmac), OSM's byway where no Definitive Map
is open, or a closure the map can't read. Usable, lightly
penalised and always flagged on the Loop; a Mark or a Deviation settles it.
_Avoid_: unknown access, maybe, grey area.

**Push**:
A very short stretch of public footpath (around 10 m) on a Gravel Ride, walked
with the bike, where it completes an otherwise impossible link. The only exception
to Access.
_Avoid_: dismount, walk section, shortcut.

**Opening Hours**:
The times a gated way such as a park or cemetery can be used. Ways known to be
closed while the user would be on them are left out; ways with opening hours,
known or suspected, are always flagged on the Loop.
_Avoid_: gate times, closing time.

**Definitive Map**:
A county council's legal record of public rights of way. The authority on whether
a way is a footpath, bridleway or byway when other sources disagree.
_Avoid_: rights-of-way map, council map.

## What the user knows

**Outing**:
A past run, walk, hike or ride the user recorded, taken from their activity
history, and kept as what it was. Walks and hikes inform Run; road rides inform no
Activity but are kept, since the user chose their way. Only Outings in England and
Wales count.
_Avoid_: activity (that word means Run or Gravel Ride here), workout, session,
track.

**Gap**:
A stretch of an Outing more than 25 m from any Segment: a path OSM may be
missing, ground with no paths on it, or GPS gone astray. Listed with its length.
_Avoid_: hole, missing path (it may not be one).

**Stars**:
One to five for an Outing or a Saved Route. **The app's stars** come from its
score (a Run's with what its off-road share and climbing add, as Loops are
ranked), banded among the user's Outings of that Activity: a Run's a fifth
each moved up half a star, a ride's a fifth each (the cuts kept by `wend outings
calibrate`); **the user's stars**
are given by a click. Two or more apart, the route stands out: where the app's
judgement and the user's part.
_Avoid_: rating (ambiguous whose), score (the number the stars come from).

**Turn point**:
A junction on a route where there's a choice of way and the route turns 45
degrees or more over the 30 m either side, said in words ("Left onto the
footpath"). Exported as GPX waypoints and TCX course points.
_Avoid_: cue (Footpath's and Komoot's own), instruction, direction.

**Route gap**:
A stretch of a route made from a track (an imported GPX, an Outing made a route)
that couldn't be matched onto the Network, drawn along the track's own points.
Not a **Gap**: the ways may be there.
_Avoid_: gap on its own (that word is an Outing's).

**Map gap**:
A path that dead-ends within 15 m of another way, with nothing mapped across
(a fence, wall, hedge, waterway or railway), joined to it as a Segment of its
own: usually two ways that should meet in OSM but don't. **Joined** under
5 m, used like its path at a little more cost; **possible** from 5 m to 15 m,
used only when the user includes it. Drawn in red, and noted on the Loop card
and in the GPX, as the user should check it's passable.
_Avoid_: gap on its own (that word is an Outing's), link, bridge (a bridge is a way).

**Chosen Outing**:
An Outing whose route the user picked themselves, as opposed to a race, a club
run following someone else, or a commute. Only Chosen Outings yield Way Choices.
_Avoid_: own run, free run, genuine run.

**Way Choice**:
A point on a Chosen Outing where the user could have gone two or more ways and
still finished at about the same length: the way taken, and the ways passed up
(the alley over the high street, the footbridge over the lights). Way Choices,
taken together, calibrate the scoring. How often a route was run, and how much
longer one way was than another, are not evidence. An Outing that followed a
Saved Loop is not a Chosen Outing: only its Deviations count.
_Avoid_: detour evidence, training data, preferences, history.

**Deviation**:
A place where an Outing that followed a Saved Loop left the planned line. The
user says why (Blocked, Grim, found better, or just felt like it), which becomes a
Mark and also counts as a Way Choice. The Loop as a whole is never rated:
choosing to run it was the verdict.
_Avoid_: detour, off-route, divergence.

**Mark**:
The user's own verdict on a Segment, overriding what the Network says about it:
**Blocked** (never used until cleared), **Grim** (heavily penalised but allowed)
or **Gem** (favoured). A Mark belongs to the Activity it was made on unless the
user says it applies to both, because a cycling ban or a flight of steps says
nothing about a Run. A Mark may apply only in certain months, because mud is the
usual reason a path goes bad.
_Avoid_: rating, flag, report, review.

**Pace**:
The user's usual speed for an Activity, taken from their Outings. Used to judge
when a Loop will reach a way with Opening Hours, not just when it starts.
_Avoid_: speed, tempo.

**Familiarity**:
How often the user has already covered a stretch of ground, counted across all
their Outings. How much it matters to a Request is set by its Novelty Lean.
_Avoid_: history, heatmap.

## Maps

**Network**:
Every way a Loop could use, together with what is known about each one: Access,
surface, traffic and Obscurity. What the generator searches.
_Avoid_: graph, map data, OSM.

**Off-road network**:
Off-road and Traffic-free Segments joined to one another, and across a single
road Segment of up to 100 m between them (a crossing, a bridge), the same 100 m
that doesn't break Flow. Its size says how long a Loop can stay off roads there:
Richmond Park's is large, a pocket park's is not. Shown on the map, coloured by
size, so the user can see where the good off-road ground is.
_Avoid_: trail system, cluster, green space.

**Trailhead**:
A place to start a Loop when driving there: a car park the public may use,
beside an Off-road network of at least 10 km and a park, common, wood or heath
of at least 5 ha, none within 250 m of another on its network. Offered as the
"car parks around here" for the part of the map in view; the user judges how far
away they are.
_Avoid_: car park (in code), start point, access point.

**Place** (search):
A name or postcode the map can be taken to (OS Open Names: towns, villages,
woods, hills, water, stations, postcodes and postcode districts; and the map's
own named parks and commons). A postcode is exact, so choosing one makes it the
start; any other place only moves the map.
_Avoid_: address, location, geocode.

**Network slice**:
The part of the Network around a Request's start that is loaded into memory to
generate its Loops; its largest connected piece is where Loops are made.
_Avoid_: subgraph, tile, region.

**Path** (in code):
The cheapest way between two points of a Loop, found by the router: from the
start to an Anchor, between Anchors, or back. A chain of Traversals.
_Avoid_: route, leg.

**Traversal** (in code):
One Segment as a Path uses it, in one direction.
_Avoid_: step, edge.

**Basemap**:
The map drawn underneath a Loop so the user can judge it. Only a picture: it
plays no part in generating Loops.
_Avoid_: map, tiles, background.

## Out on a Loop (version 2)

**Decision Point**:
A junction where a Loop leaves the obvious line, such as turning into an alley
rather than carrying straight on. Most junctions are not Decision Points.
_Avoid_: turn, junction, waypoint.

**Cue**:
A short heads-up while the user is out on a Loop: a Decision Point, an Attraction,
a hazardous Crossing, a way with Opening Hours, or the start of unfamiliar ground.
Never a running commentary.
_Avoid_: instruction, direction, turn-by-turn, alert.
