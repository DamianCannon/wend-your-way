# Behavioural specification

What Wend Your Way must do, observably. Terms are from [CONTEXT.md](../CONTEXT.md);
how it's built is in [ARCHITECTURE.md](ARCHITECTURE.md). Each requirement is
written to be testable. **[FC]** marks the first cut; **[V1]** marks the rest of
version 1; **[Later]** marks what comes after it.

## 1. Requests and Loops

- **R1.1 [FC]** A Request takes a start point, an Activity, a Distance in km,
  four Leans (Greenery, Off-road, Hills, Novelty), a Heading and a start time
  (defaulting to now). In the first cut, Run is the only Activity offered.
- **R1.1a [FC]** The start point, Activity, Distance, Leans and Heading of the
  last Request are restored when the app is next opened. Before any Request,
  the start is home, every Lean is at its centre except Novelty (at zero), and
  there is no Heading.
- **R1.2 [FC]** A Request whose start point is outside England and Wales is
  refused with a message saying so. No Loops are generated.
- **R1.2a [FC]** The start point snaps to the nearest Segment the Activity can
  use that is not a Main Road. It never snaps across a railway, river or
  motorway to a Segment on the far side.
- **R1.3 [FC]** Every offered Loop starts and finishes at the Request's start
  point, and its length is no more than **1 km** shorter than the Distance and no
  more than **15%** longer, or 1 km if that's more (user, 2026-10-24: 10% more
  for the river is worth it). A Loop outside that window is never offered.
- **R1.4 [FC]** A Request offers up to **five** Loops, ordered best first, which
  the user pages through one at a time. No two offered Loops share more than
  **30%** of their ground, measured by length over same-ground groups. If fewer
  than five distinct Loops exist, fewer are offered, and the pager shows how
  many. While fewer than five are good enough (each after the first scoring at
  least half the first's, with no more than 200 m more Main Road), the next
  best heading where none offered goes are added after them, below that bar
  but scoring at least 0.3 of the first's with no more than 2 km more Main
  Road, each marked "In another direction, below the usual bar" (T096,
  STORY-044).
- **R1.5 [FC]** If no Loop at all can be found, the user is told so plainly. The
  generator never offers a Loop that breaks R1.3 or the Access rules just to have
  something to show.
- **R1.6 [FC]** The first Loop appears within **10 seconds**, and generation
  finishes within **30 seconds**. While it runs, the best Loops found so far are
  shown and replaced as better ones are found.
- **R1.7 [FC]** Every Loop uses only ways its Activity has Access to at the
  time the user will reach them. On a Run, ways with Access `forbidden` are never
  used.
- **R1.8 [FC]** The time the user reaches each part of a Loop is estimated from
  the start time and their Pace for the Activity: the median of their Chosen
  Outings, or 6 min/km for a Run before any Outings are imported.

## 2. Scoring rules (observable through the Breakdown and through choice)

- **R2.1 [FC]** A **Main Road** costs five times an ordinary road per metre, so a
  Loop takes a quiet way round up to about five times as long and uses a Main
  Road only for a short link (user, 2026-10-19: a cost, not R2.1's first outright
  ban, which left some starts with no Loop). Nothing green beside it lowers that
  cost. The Breakdown gives the Loop's Main Road length; highlighting the stretch
  on the map is still to come.
- **R2.2 [FC]** Between two otherwise equal Loops, the one with more Off-road
  ground scores higher. Between equal Off-road shares, the one with the longer
  unbroken Off-road or Traffic-free stretches (Flow) scores higher.
- **R2.2a [FC]** Between two otherwise equal Loops, the one with more Greenery
  scores higher. A paved path across a park outscores an identical paved path
  beside a busy road.
- **R2.3 [FC]** Exposure is length × Hazard summed over Connectors. A short
  stretch of a hazardous road can score better than a long stretch of a moderate
  one.
- **R2.4 [FC]** A Retrace is detected by same ground, not by identical ways.
  Returning along the other pavement of the same road, or along a path within
  12 m of the first, counts as a Retrace. Retraced metres cost four times their
  ground's own rate, without Greenery or Corridor discounts, and never lengthen a
  Flow stretch (T037 review).
- **R2.5 [FC]** A Stem (a Retrace starting at the start point) is penalised less
  per metre than any other Retrace.
- **R2.6 [FC]** A Loop has at most one Spur, and a Spur must reach an Attraction.
  Loops with two or more Spurs, or a Spur that reaches no Attraction, are never
  offered.
- **R2.7 [FC]** A figure-of-eight that crosses its own line without repeating
  ground has no Retrace penalty.
- **R2.8 [FC]** Scoring weights are fixed in code. Nothing in the UI changes
  them. Leans adjust one quality's contribution for the current Request only,
  within bounds set in code; at its centre a Lean has no effect.
- **R2.8a [FC]** Moving a Lean towards a quality never lowers that quality's
  share in the top-ranked Loop drawn from the same candidates. For example,
  raising the Greenery Lean never makes Loop 1's Greenery % fall.
- **R2.8b [FC]** No Lean can introduce a Main Road into an offered Loop (R2.1
  still applies) or make a way usable that Access forbids.
- **R2.9 [FC]** A Loop's score is per km: two Loops with identical ground
  qualities score the same whatever their lengths.
- **R2.10 [FC]** Routing and scoring use the same weights: changing a weight
  changes both which ways the generator prefers and how Loops score.
- **R2.11 [FC]** With a Heading at full strength, every offered Loop's centre
  of mass lies within **45°** of the Heading's bearing from the start, unless
  no such Loop exists, in which case the Breakdown says so. With no Heading, the
  bearing has no effect on scoring.
- **R2.12 [FC]** The Hills Lean, above its centre, favours Loops with more
  climbing per km; below its centre, it favours Loops with less.

## 3. The Breakdown

- **R3.1 [FC]** Every offered Loop shows a Breakdown with: total length; climbing
  (total ascent, from OS Terrain 50); Off-road %; Traffic-free %; Greenery %;
  Connector length; the longest unbroken Off-road or
  Traffic-free stretch (with its Corridor name, if it has one); Exposure; the
  number of Crossings, each listed with its kind and the road it crosses; and
  Retrace length (Stem shown separately).
- **R3.2 [FC]** Ways with Opening Hours (known or suspected) are listed in the
  Breakdown and marked on the map, with the hours where known. Hours are
  **known** when OSM tags them (on the way, or on the park or cemetery containing
  it); "dusk" and "dawn" mean local sunset and sunrise on the Request's date.
  Hours are **suspected** when a park or cemetery has a gate on its boundary but
  no hours tagged.
- **R3.3 [FC]** When Novelty is above zero, the Breakdown shows the length and
  share of unfamiliar ground, and the unfamiliar stretches are highlighted on
  the map.
- **R3.4 [FC]** Each Crossing and each Connector shown in the Breakdown can be
  located on the map by selecting it.
- **R3.5 [FC]** The Breakdown includes an elevation profile of the Loop, with
  total ascent and descent.

## 4. Novelty and Familiarity

- **R4.1 [FC]** With Novelty at or above its midpoint, every offered Loop has at
  least **10%** of its
  length on ground no Outing (of the same Activity) has covered. If no such Loop
  exists, the best Loops are offered anyway, each showing its actual unfamiliar
  share and a note saying the target wasn't met.
- **R4.2 [FC]** With Novelty at zero, Familiarity has no effect on scoring.
  Between zero and the midpoint, unfamiliar ground is favoured without a
  target.
- **R4.3 [FC]** Familiarity treats same ground as one: if an Outing covered one
  pavement of a road, the other pavement counts as familiar too.

## 5. Saved Loops and GPX

- **R5.1 [FC]** Any offered Loop can be saved with a name. Saved Loops are listed,
  can be reopened on the map with their Breakdown, renamed and deleted.
- **R5.2 [FC]** Any offered or Saved Loop can be exported as a GPX 1.1 file
  containing a single track, which opens in the OS Maps app.
- **R5.3 [FC]** Offered Loops that aren't saved are gone after the next Request,
  or when the app restarts. (A Request's settings are remembered; its Loops are
  not.)
- **R5.5 [Built, PLAN T099]** To phone: the Loop card's QR button shows a QR
  code of a link to the Loop shown, as GPX, served on the home network by a
  listener of its own (the app stays on the laptop): one download, for ten
  minutes, counted down on the card.
- **R5.6 [Built, PLAN T100]** Turn points: an exported route marks each turn
  worth saying (a junction with a choice where it turns 45 degrees or more,
  measured over 30 m either side), in words ("Left onto Bush Road", "Right
  onto the footpath"): waypoints in the GPX, course points in a TCX course
  for a watch (TCX, in the Loop card's bar beside QR and GPX).
- **R5.4 [Built, PLAN T080, T085, T103]** Saved Loops survive a Network
  rebuild. A Saved Loop whose ways have changed in OSM (a Segment of it, by
  its way and end nodes, no longer on the Network) says "The map has changed
  under this route since it was saved" in Saved Routes, and opened can't be
  nudged; its line is kept as it was, never silently altered.

## 6. Outings import and triage

- **R6.1 [FC]** `wend outings import-strava <zip>` imports a Strava bulk export. It keeps
  runs (and rides, stored for later) that have GPS and lie in England or Wales,
  and reports how many were imported, skipped (no GPS, abroad, Scotland, other
  sport) and failed (with reasons).
- **R6.2 [FC]** Re-importing the same export, or a newer one, adds new Outings
  only. No Outing is duplicated, and categories already set are kept.
- **R6.3 [FC]** Imported Outings get a suggested category: Race if the name
  suggests a race or parkrun, Commute if Strava flagged it, otherwise Unreviewed.
- **R6.4 [FC]** The triage screen shows one Outing at a time on the map, with its
  name, date, distance and suggested category. The user can confirm or change the
  category (Chosen, Race, Club run, Commute, Ignore) with a single click or
  keystroke, and move to the next.
- **R6.5 [FC]** Each Outing records which Segments it covered (most of the
  Segment's length within ~20 m of the track, on a similar bearing) and its
  coverage: the share of its track near covered Segments. Outings below the
  coverage threshold are flagged on the triage screen and excluded from
  Familiarity until the user accepts them.
- **R6.7 [FC]** Each Outing's **gaps** (stretches of track more than 25 m from
  any Segment) are shown on the map and listed with their lengths, as places
  where OSM may be missing a path.
- **R6.6 [Dropped, T109]** ~~From the triage screen, the user can make any
  Chosen Outing a good or bad Reference Loop.~~ The user's own stars on every
  Outing (R6.8) do this job.
- **R6.8 [Built, PLAN T097, STORY-045]** Stars. Each Outing and Saved Route
  shows the app's stars, one to five: its track matched onto the Network and
  scored as a Loop is. A Run's stars come from its ranking value (its score
  plus what its share off-road and climbing per km add, T105), cut at the
  10th, 30th, 50th and 70th percentiles of the user's matched Runs (T108,
  2026-11-11: fifths moved up half a star, to match the user's); a ride's
  from its score, a fifth of the user's rides each. The cut points are
  worked out again by `wend outings calibrate` (T114) and kept with the user's
  data; until then, the ones measured last, in code. One matched
  for less than 80% of its length shows "–"; one not scored yet says so (an
  Outing or a Saved Route saved before the stars until `wend outings score`
  runs, which scores such a Saved Route as a Run, T115). The user gives each one to five stars of their
  own, kept with it; where theirs and the app's are 2 or more apart, it
  stands out. Starred routes on London's Network join the rated-Loop guard
  (run by name, T115):
  the app mustn't score one above another of the same Activity the user
  gave 2 or more stars more.

## 7. Reference Loops

The weights are judged against the user's own ratings where they are, not a
snapshot of them (T109, T113): see ARCHITECTURE, "Scoring weights and the
user's ratings".

- **R7.1 [Dropped, T109]** ~~`wend reference snapshot` writes every Reference
  Loop, with its Segment attributes, to the test fixtures.~~
- **R7.2 [Built, T032]** The test suite contains a specification that **every
  Loop the user called very good scores higher than every one they called
  good** under the current weights (`RankingTheUsersRatedLoops`). It reads the
  built Network, so needs PostgreSQL, and is skipped without it. The user's
  starred routes are judged the same way (R6.8).
- **R7.3 [Dropped, T109]** ~~The app can show every Reference Loop's score and
  Breakdown side by side.~~ The Outings panel shows the app's stars and the
  user's side by side, and where they part.
- **R7.4 [Dropped, T109]** ~~A ranking specification on saved slices.~~

## 8. Network and data

- **R8.1 [FC]** `wend network build` rebuilds the Network from the latest OSM
  extract, OS Terrain 50 and DfT traffic counts without touching any user data
  (Outings, Marks, Saved Loops, settings).
- **R8.1a [FC]** While a build runs, the app keeps generating Loops on the
  previous Network. A build that fails leaves the previous Network in use. After
  a successful build, `wend network rollback` restores the previous Network.
- **R8.2 [FC]** After a rebuild, user data that refers to a way whose OSM identity
  no longer exists is re-attached to the closest matching Segment, or flagged as
  orphaned if none is close enough. Nothing is silently dropped.
- **R8.3 [FC]** The Network covers England and Wales only. The Basemap shows OSM
  attribution and Thunderforest attribution at all times.
- **R8.4 [FC]** `wend setup check` reports whether PostgreSQL, PostGIS,
  osm2pgsql, the Network and the Thunderforest key are each present and working,
  with a fix hint for each one that isn't.
- **R8.6 [Built, PLAN T102]** `wend backup` writes a dated dump of all user data
  (the `app` schema) to a folder the user chooses (else OneDrive), and the
  dump restores with `wend restore`, after a backup of what was there; a backup
  from a newer Wend Your Way is refused. Backed up by itself after imports,
  scoring and builds, and each day the app starts; the last 10 kept.
- **R8.7 [Built, PLAN T127, STORY-047]** `wend update` brings the map up to date
  in one go: when Geofabrik's Great Britain extract is newer than the map, it
  downloads it (checked against its MD5, the one before kept), builds the
  Network from it, scores the Outings and Saved Routes not scored, calibrates
  the stars and backs up. One update or build at a time. Whatever fails part-way,
  the map in use stays as it was: a download cut off, stalled for 2 minutes or
  not matching its MD5 changes nothing; an import that didn't finish is marked
  so, and a rebuild from it is refused; the build is all or nothing; a run that
  stopped after its new map was swapped in is finished by the next, without a
  download. Each stage, a heartbeat and how it ended are kept.
- **R8.8 [Built, PLAN T128, STORY-047]** Under the map's date the Request panel
  says whether Geofabrik has a newer map, and **Update the map** starts `wend
  update` in a process of its own, which carries on if the app is closed; while
  it runs, its stage; after, how it ended, or, stopped part-way, where. It won't
  start a `wend` that isn't built or was built from other code than the app's.
- **R8.5 [V1]** The first time a Request or an Outing touches a highway authority
  whose Definitive Map hasn't been fetched, it's fetched from rowmaps in the
  background and used to correct Access from then on. Authorities with no
  published data are recorded as such and not retried on every Request.

## 9. Rest of version 1

- **R9.1 [V1] Gravel Ride.** Requests can use Gravel Ride. Gravel Ride Loops never
  use public footpaths, except Pushes of at most about 10 m, each listed in the
  Breakdown. Ways with Unconfirmed Access may be used, are flagged, and are listed
  in the Breakdown. Obstacles are listed with their kind, and cost more than on a
  Run. *As built (PLAN T068):* Run or Gravel Ride is chosen in the Request
  panel; a Gravel Ride may use roads (not motorways), cycleways, bridleways,
  byways and tracks, never footways, pedestrian streets, steps or public
  footpaths, and a `bicycle` tag decides where there is one; an untagged path or
  open ground is Unconfirmed Access, shown in the Breakdown in hi-vis. Its costs
  are a Run's for now. *Obstacles (PLAN T069):* gates, cycle barriers, kissing
  gates, turnstiles, motorcycle barriers, stiles, cattle grids and fords cost a
  Gravel Ride Off-road metres, as Crossings do, nothing on a Run, and are listed
  by kind on the Loop card. Pushes are measured and held: a short footpath joins
  two pieces nothing else joins too rarely to be worth it yet.
- **R9.2 [V1] Marks.** Any Segment on the map can be Marked Blocked, Grim or Gem,
  for the current Activity or both, optionally for chosen months. A Blocked
  Segment is never used by a Loop of an Activity it applies to, in the months it
  applies to. A Mark can be cleared. Marks are shown on the map.
- **R9.3 [V1] Nudges.** On an offered or Saved Loop, the user can select a point
  or Segment and choose "go this way" (Via) or "not this" (Avoid). The Loop is
  repaired around the change within **3 seconds**. The parts away from the change
  are unaltered, and the Breakdown is updated. If no repair keeps the Loop within
  the Distance window, the user is told, and the original Loop is kept.
- **R9.4 [V1] Following a Saved Loop.** Importing an Outing that followed a Saved
  Loop links the two and updates Familiarity. Deviations are found by ordered
  map-matching (designed in version 1). Each Deviation is shown in turn
  with the question "Why did you leave here?" and the answers Blocked / Grim /
  Found better / Just felt like it. Blocked or Grim Marks the skipped Segment;
  Found better Marks the new way a Gem. Answering takes one click per Deviation.
- **R9.5 [V1] Calibration.** Way Choices are extracted from Chosen Outings and
  used to propose weight changes. A proposed change is accepted only if the
  rated-route guards (R7.2, R6.8) still pass. TODO: specify extraction and
  how proposals are presented.

- **R9.6 [Built, PLAN T020-T021] Trail Ride.** Requests can use Trail Ride. A Trail Ride Loop uses
  only roads and ways with a recorded motor vehicle right, never a public footpath,
  bridleway or restricted byway, and never a way closed by a Traffic Regulation
  Order at the time it would be ridden, as far as OSM records it. A byway is one on
  a highway authority's Definitive Map; an unclassified green lane, or an OSM byway
  where no Definitive Map is open, is Unconfirmed: used, flagged "check this"
  (user, 2026-11-08). Roads are Connectors, and Main Roads aren't excluded;
  motorways are. No open list of TROs exists: every Trail Ride says to check
  closures before riding (PLAN T020, T021).

## 11. Finding where to start [Built, PLAN T022–T023, T060–T063]

Added 2026-10-18 at the user's request: they'd happily drive 20 or 30 minutes to
start somewhere a Loop can be nearly all Off-road. Drive times from home were
built (T024–T025) and then removed (T065, 2026-11-03): "we can just select from
the car parks shown on the map and decide for ourself how far away they might
be" (user). Sharing a GPX (once R11.5) was dropped.

- **R11.1 Off-road networks.** The build finds every Off-road network and its
  length. An optional map layer, remembered in the browser, draws them coloured
  by size from zoom 13, and as dots for networks of 10 km or more when zoomed
  out further.
- **R11.2 Trailheads.** The build keeps as Trailheads the car parks the public
  may use (not private, customers', residents' or multi-storey) within 100 m of
  an Off-road network of 10 km or more and of a park, common, nature reserve,
  wood, forest, heath, meadow, grassland or scrub of 5 ha or more, none within
  250 m of another on the same network (the named, then the larger, kept first).
  Each is named for itself, else the nearest named green area within 500 m.
- **R11.3 Car parks around here.** From zoom 11, every Trailhead in view is
  marked on the map. The Request panel lists the best ten in view for the
  Distance: one per place (a name; an unnamed car park is its network's own
  place), the one nearest the middle of the map; ranked by their network's green
  ground up to three times the Distance, then by nearness to the middle. The list
  follows the map as the user moves it, never as the map fits a Loop. Choosing a
  car park, in the list or on the map, makes it the start and finds its Loops.
  Zoomed out further, it says to zoom in.
- **R11.4 Compare before you commit.** For the first five places listed, Loop
  1 is worked out in the background, one at a time, and its length, green share
  and climbing shown beside each ("10.6 km · 97% green · 121 m up"). Stopped
  when the list, the Distance or a Lean changes or a Request starts; kept for the
  app's run by place, Distance and Leans.
- **R11.6 Car park details.** Each car park says only what the map knows of it:
  free, a donation or pay to park; its spaces; its own opening hours as
  tagged; toilets, a café and a pub within 300 m ("Free · 250 spaces · Toilets
  · Café"). Closing times are tagged on few car parks, and none is guessed.
- **R11.7 Cafés and toilets.** An overlay in the map's layer switcher, off at
  first and remembered, shows from zoom 13 every café and public toilet mapped
  as a point in view (at most 500): a cup or "WC", named on pointing.
- **R11.5 The place on the Loop card.** Opened, the Loop card says which car
  park its Loops start from ("From Sheen Gate").

## 12. Places and Point-to-points [R12.1 built, PLAN T064; R12.2 Later]

Added 2026-10-18 at the user's request.

- **R12.1 Place search.** A search box over the map finds places by name and
  postcode from two characters, answered on the laptop from OS Open Names (OS
  OpenData, loaded by `wend places import`), so no search ever leaves it: cities,
  towns, suburbs, villages, hamlets, woods, hills, valleys, water, railway
  stations, every postcode with or without its space, postcode districts, and
  the map's own named parks, commons, nature reserves, woods and heaths. Not
  roads or addresses. Eight are shown, each with its kind and county or borough,
  ranked: the whole name, then its start, then a later word's; then cities and
  towns, then suburbs, villages, green areas, hills, water and stations
  together, then hamlets, then postcodes; then nearest the middle of the map
  (never home). Choosing a whole postcode
  makes it the start and finds its Loops; any other place only moves the map,
  at a zoom for its kind, and its car parks follow. Clicking the map shows the
  nearest postcode within 1 km in the box. What's typed is never logged or kept.
  OS, Royal Mail and National Statistics are credited beside the results. This
  is search only: OS data is never used for routing (ADR 0001).
- **R12.2 Point-to-point.** A Request can name an end as well as a start. Its
  Distance is at least the direct way's length (the screen says so otherwise),
  and up to the Distance window longer, so the Point-to-point can wander onto
  better ground. It is scored, broken down and exported like a Loop, with no
  Stem.

TODO: Anchors for a Point-to-point (on an ellipse with the start and end as its
foci, instead of a circle through the start), and how Heading applies.

## 10. The planner screen

- **R10.1 [FC]** The map fills the screen. A collapsible panel on the left has
  an icon rail for: Request, Saved Routes, Outings, Admin and Help (Reference Loops'
  panel removed, T109: the user's stars took its place).
  Collapsing the panel leaves only the rail.
- **R10.2 [FC]** The Request panel holds the start, the Distance with a Go
  button beside it, the Lean sliders, the Heading control and the car parks
  around here. A start set by clicking the map, choosing a car park or choosing a
  postcode finds its Loops at once; Go is for after changing the Distance.
  *As built:* the Hills and Off-road Leans; no Heading yet; place search is a box
  over the map (R12.1).
- **R10.3 [FC]** The Heading control is a compass circle containing a smaller
  puck. Dragging the puck sets the Heading: its angle is the bearing and its
  distance from the centre is the strength. The current Heading is shown in
  words (e.g. "NE, 60%"). Double-clicking the control, or pressing reset,
  returns the puck to the centre (no Heading).
- **R10.4 [FC]** Moving a Lean or the Heading after Loops are offered
  **re-ranks the candidates already found within half a second**, so the pager
  changes while dragging. A background search then looks for better Loops
  under the new settings and replaces pager entries as it finds them, within
  the R1.6 budget. There is no need to press Go again.
- **R10.5 [FC]** A pager card at the bottom centre shows `‹  Loop n of m  length  ›`.
  Arrow buttons and the left/right arrow keys move between Loops; the map shows
  only the current Loop.
- **R10.6 [FC]** The pager card expands upwards into the Breakdown (R3) with
  the elevation profile (R3.5) and buttons to Save and Export GPX.
- **R10.7 [FC]** The current Loop is drawn with its direction of travel shown
  and its Anchors (and any Vias) numbered in order.
- **R10.9 [Dropped, PLAN T101]** The Loop in words: built, then dropped (user,
  2026-11-10: "the route descriptions don't add anything because they're all
  very similar to let's drop that").


## TODO

- R1.3: confirm whether ±1 km also suits long Gravel Rides, or whether the window
  should grow with Distance.
- R6.5: set the match-confidence threshold after looking at real London Outings.
- R9.5: Way Choice calibration needs its own design pass.
