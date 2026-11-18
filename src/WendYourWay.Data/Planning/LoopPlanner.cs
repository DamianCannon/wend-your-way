using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using Activity = WendYourWay.Core.Loops.Activity;
using WendYourWay.Core.Loops;
using WendYourWay.Core.Network;
using WendYourWay.Core.Routing;
using WendYourWay.Core.Scoring;

namespace WendYourWay.Data.Planning;

/// <summary>
/// Loads the slice a Request needs, generates off the caller's thread, and converts the result for
/// the map, with its elevation profile when OS Terrain 50's tiles are given.
/// </summary>
public sealed class LoopPlanner(INetworkSlices slices, MapGeometry geometry, ScoringWeights weights, TerrainGrid? terrain = null, RouteMatching? matching = null) : ILoopPlanner
{

    // The slice an imported route is matched on reaches this far beyond it, for its Nudges.
    private const double ImportMarginM = 1000;

    // No route's slice reaches further than a 100 km Request's (T086): 46.5 km.
    private static readonly double FurthestM = new Request(new NodePoint(0, 0), Request.MaxDistanceKm).SliceRadiusM;

    // Under this share of an imported route along what was matched, it isn't shown.
    private const double MatchedAtLeast = 0.5;

    /// <summary>Why a reopened Saved Route isn't nudged when the Network has changed under it (T085).</summary>
    public const string MapChanged = "The map has changed under this route since it was saved, so it can't be nudged.";

    // A saved stop or Avoid this near a junction or a Segment's middle is there.
    private const double KeptWithinM = 10;

    public async Task<IOfferedLoops> ReopenAsync(
        PlannedLoop saved, IReadOnlyList<(SegmentKey Key, bool Forward)> traversals, Activity activity = Activity.Run, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(saved);
        ArgumentNullException.ThrowIfNull(traversals);
        if (traversals.Count == 0 || saved.Line.Count < 2) throw new RouteNotMatchedException(MapChanged);

        // The slice round it, as an imported route's: out to its furthest point plus the margin.
        var line = await geometry.ToGridAsync(saved.Line, cancellationToken);
        var centre = new NodePoint((line.Min(p => p.X) + line.Max(p => p.X)) / 2, (line.Min(p => p.Y) + line.Max(p => p.Y)) / 2);
        var radiusM = line.Max(centre.DistanceTo) + ImportMarginM;
        if (radiusM > FurthestM) throw new RouteNotMatchedException(RouteMatching.TooSpread);
        var slice = await slices.AroundAsync(centre, radiusM, activity, cancellationToken: cancellationToken);

        // Its Segments by OSM identity: every one, or the map has changed under it. Of a Main Road's
        // sides, those that join up, and its crossings put back (T145c).
        var kept = Corners.Rejoin(slice, traversals) ?? throw new RouteNotMatchedException(MapChanged);
        int StartOf(Traversal t) => t.Forward ? slice.Segments[t.Segment].From : slice.Segments[t.Segment].To;
        int EndOf(Traversal t) => t.Forward ? slice.Segments[t.Segment].To : slice.Segments[t.Segment].From;

        // Its stops at the junctions it reaches, in turn; its Avoids at the Segments they marked.
        var reached = kept.Select(EndOf).Distinct().ToArray();
        var anchors = new List<int>();
        foreach (var stop in await geometry.ToGridAsync(saved.Anchors, cancellationToken))
        {
            var nearest = reached.MinBy(n => slice.Node(n).DistanceTo(stop));
            if (slice.Node(nearest).DistanceTo(stop) <= KeptWithinM) anchors.Add(nearest);
        }
        var avoided = new List<int>();
        foreach (var avoid in await geometry.ToGridAsync(saved.Avoids, cancellationToken))
        {
            var (best, bestM) = (-1, double.MaxValue);
            for (var s = 0; s < slice.Segments.Count; s++)
            {
                if (slice.Segments[s].IsCrossingLink) continue;
                var (a, b) = (slice.Node(slice.Segments[s].From), slice.Node(slice.Segments[s].To));
                var m = new NodePoint((a.X + b.X) / 2, (a.Y + b.Y) / 2).DistanceTo(avoid);
                if (m < bestM) (best, bestM) = (s, m);
            }
            if (bestM <= KeptWithinM) avoided.Add(best);
        }

        var found = new Found(this, slice, activity, null, null) { Gaps = saved.Gaps, Gpx = new GpxPoints(saved.Line, line) };
        return found.Reopened(saved, new Loop(StartOf(kept[0]), anchors, kept), avoided);
    }

    /// <summary>Why a Point-to-point whose start and end are too far apart isn't planned (T090).</summary>
    public const string TooFarApart = "The start and end are too far apart to plan between at once.";

    /// <summary>Why a Point-to-point with no way between its start and end isn't planned (T090).</summary>
    public const string NoWay = "There's no way from the start to the end on the ways this Activity can use.";

    /// <summary>Why a Point-to-point's Distance is too short (T090): the least it can be.</summary>
    public static string AtLeast(double shortestM) => string.Create(CultureInfo.InvariantCulture, $"At least {Math.Ceiling(shortestM / 100) / 10:0.0} km from the start to the end.");

    // With no Distance, how much longer than straight the shortest way is taken to be at most,
    // to size the slice: the longest offered is LongestTimesShortest of it. 1.4 read a quarter more
    // of London than 1.25 and took twice as long to search, for the same routes (Wimbledon Common
    // to Clapham Common, whose shortest way is 1.11 times straight, 2026-11-08); where the
    // shortest way winds more, the longest routes are only what the slice holds.
    private const double ShortestDetourAtMost = 1.25;

    public async Task<IOfferedLoops> PlanPointToPointAsync(
        LatLon start, LatLon finish, double? distanceKm = null, Leans? leans = null, Activity activity = Activity.Run, CancellationToken cancellationToken = default)
    {
        leans ??= Leans.Centre;
        var (from, to) = (await geometry.ToGridAsync(start, cancellationToken), await geometry.ToGridAsync(finish, cancellationToken));
        var request = new PointToPointRequest(from, to, distanceKm, leans.Hills, leans.OffRoad, activity);
        if (!await geometry.InEnglandAndWalesAsync(from, cancellationToken) || !await geometry.InEnglandAndWalesAsync(to, cancellationToken))
            throw new OutsideEnglandAndWalesException();

        // No point of a route of length L lies further than L / 2 from the middle of its ends.
        var longestM = request.LongestKm * 1000 ?? PointToPointGenerator.LongestTimesShortest * ShortestDetourAtMost * from.DistanceTo(to);
        var radiusM = (longestM / 2) + Request.SnapMarginM;
        if (radiusM > FurthestM) throw new PointToPointRefusedException(TooFarApart);
        var middle = new NodePoint((from.X + to.X) / 2, (from.Y + to.Y) / 2);
        var slice = await slices.AroundAsync(middle, radiusM, activity, cancellationToken: cancellationToken);

        var found = await Task.Run(() => PointToPointGenerator.Find(slice, request, weights, cancellationToken), cancellationToken);
        if (found.ShortestM is not { } shortestM) throw new PointToPointRefusedException(NoWay);
        if (found.Offered.Count == 0 && distanceKm * 1000 < shortestM) throw new PointToPointRefusedException(AtLeast(shortestM));
        return await new Found(this, slice, activity, null, null).PointToPointsAsync(found.Offered, shortestM, cancellationToken);
    }

    public async Task<IOfferedLoops> ImportAsync(IReadOnlyList<LatLon> route, Activity activity = Activity.Run, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(route);
        if (matching is null) throw new InvalidOperationException("This planner wasn't given a RouteMatching to import with.");
        // One point for each place, so the matcher's points are the route's, in step.
        LatLon[] points = [.. route.Where((p, i) => i == 0 || p != route[i - 1])];
        if (points.Length < 2) throw new RouteNotMatchedException("That GPX has too few points to follow.");
        var (first, last) = (await geometry.ToGridAsync(points[0], cancellationToken), await geometry.ToGridAsync(points[^1], cancellationToken));
        if (!await geometry.InEnglandAndWalesAsync(first, cancellationToken)) throw new OutsideEnglandAndWalesException();

        var matched = await matching.MatchAsync(points, activity, ImportMarginM, first.DistanceTo(last) <= PlannedLoop.ClosedWithinM, FurthestM, cancellationToken);
        if (matched.Match.Loop.Traversals.Count == 0 || matched.ShareNearMatch < MatchedAtLeast)
            throw new RouteNotMatchedException("Under half of that route follows the map: too few points to follow, or mostly off paths Wend Your Way knows.");

        var found = new Found(this, matched.Slice, activity, null, null) { Gaps = GapsOf(matched, points), Gpx = new GpxPoints(points, matched.Points) };
        return await found.ImportedAsync(matched.Match.Loop, cancellationToken);
    }

    // Each stretch the matcher couldn't join, along the route's own points from where it starts.
    private static RouteGap[] GapsOf(MatchedRoute matched, LatLon[] points)
    {
        var along = new double[matched.Points.Count];
        for (var i = 1; i < along.Length; i++) along[i] = along[i - 1] + matched.Points[i].DistanceTo(matched.Points[i - 1]);
        return [.. matched.Match.Unjoined.Select(gap =>
        {
            var end = gap.RoutePoint;
            while (end + 1 < along.Length && along[end] - along[gap.RoutePoint] < gap.RouteM) end++;
            return new RouteGap(points[gap.RoutePoint..(end + 1)], gap.RouteM);
        })];
    }

    // How often a Request reports how far it has got, and offers the Loops found so far.
    private static readonly TimeSpan ReportEvery = TimeSpan.FromSeconds(0.2);
    private static readonly TimeSpan OfferEvery = TimeSpan.FromSeconds(1);

    public async Task<IOfferedLoops> PlanAsync(
        LatLon start, double distanceKm, Leans? leans = null, Activity activity = Activity.Run, Action<PlanningProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        leans ??= Leans.Centre;
        var request = new Request(await geometry.ToGridAsync(start, cancellationToken), distanceKm, leans.Hills, leans.OffRoad, activity);
        if (!await geometry.InEnglandAndWalesAsync(request.Start, cancellationToken)) throw new OutsideEnglandAndWalesException();
        var watching = new Watching();
        var clock = Stopwatch.StartNew();

        var reading = slices.AroundAsync(request.Start, request.SliceRadiusM, request.Activity, progress is null ? null : watching.Read, cancellationToken);
        // Each wait ends at once when the Request is cancelled: stop then, not go round without waiting.
        while (progress is not null && await Task.WhenAny(reading, Task.Delay(ReportEvery, cancellationToken)) != reading)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (watching.Reading is { } read) progress(new(PlanningStage.Reading, read.Read, read.Total, Left(clock.Elapsed, read.Read, read.Total), null));
        }
        var slice = await reading;
        if (progress is not null && watching.Reading is { } all) progress(new(PlanningStage.Reading, all.Read, all.Total, TimeSpan.Zero, null));

        Found? kept = null;
        var found = await TryAsync(slice, request, progress, async soFar =>
        {
            kept ??= new Found(this, slice, request.Activity, request, soFar);
            kept.Now(soFar);
            return await kept.OfferAsync(leans, cancellationToken);
        }, cancellationToken);
        progress?.Invoke(new(PlanningStage.Drawing, 0, null, null, null));
        kept ??= new Found(this, slice, request.Activity, request, found);
        kept.Now(found);
        return await kept.OfferAsync(leans, cancellationToken);
    }

    /// <summary>
    /// Finds the Loops for a Request off the caller's thread (it's CPU-bound for up to a few
    /// seconds), saying how many it has tried a few times a second; and, given
    /// <paramref name="offer"/>, the Loops offered from those found so far: at once, then about
    /// once a second.
    /// </summary>
    private async Task<FoundLoops> TryAsync(
        NetworkSlice slice, Request request, Action<PlanningProgress>? progress, Func<FoundLoops, Task<IOfferedLoops>>? offer, CancellationToken cancellationToken)
    {
        var watching = new Watching();
        var clock = Stopwatch.StartNew();
        var finding = Task.Run(() => LoopGenerator.Find(slice, request, weights, progress is null ? null : watching.Searched, cancellationToken), cancellationToken);
        IOfferedLoops? soFar = null;
        var offeredAt = TimeSpan.MinValue;
        while (progress is not null && await Task.WhenAny(finding, Task.Delay(ReportEvery, cancellationToken)) != finding)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (watching.Search is not { } search) continue;
            if (offer is not null && search.SoFar.Count > 0 && (soFar is null || clock.Elapsed - offeredAt >= OfferEvery))
            {
                soFar = await offer(search.SoFar);
                offeredAt = clock.Elapsed;
            }
            // No time left while trying: the rate swings between batches of circles and the slower
            // destination Loops, 4-15 times out at first, a third at the end (measured 2026-10-26).
            progress(new(PlanningStage.Trying, search.Tried, search.ToTry, null, soFar));
        }
        var found = await finding;
        if (progress is not null && watching.Search is { } last) progress(new(PlanningStage.Trying, last.Tried, last.ToTry, null, soFar));
        return found;
    }

    // About how long is left at the rate so far, once there's a rate and a total.
    private static TimeSpan? Left(TimeSpan elapsed, int done, int? total) =>
        done > 0 && total is { } all ? elapsed * ((double)(all - done) / done) : null;

    // What a Request has heard from reading and searching since it last reported, from whichever
    // thread told it.
    private sealed class Watching
    {
        private readonly Lock _gate = new();
        private SliceReading? _reading;
        private SearchProgress? _search;

        public SliceReading? Reading
        {
            get
            {
                lock (_gate) return _reading;
            }
        }

        public void Read(SliceReading reading)
        {
            lock (_gate) _reading = reading;
        }

        /// <summary>The latest from the search.</summary>
        public SearchProgress? Search
        {
            get
            {
                lock (_gate) return _search;
            }
        }

        public void Searched(SearchProgress search)
        {
            lock (_gate) _search = search;
        }
    }

    // What a Request found, at its own Lean and any searched since, kept for re-ranking, with every
    // Loop traced for the map so far: the slice stays in memory as long as the Loops offered do. Or
    // an imported route (T083): no Request, nothing found, the one Loop to nudge.
    private sealed class Found(LoopPlanner planner, NetworkSlice slice, Activity activity, Request? request, FoundLoops? found)
    {
        /// <summary>An imported route's gaps (T083), drawn with it however it's nudged.</summary>
        public IReadOnlyList<RouteGap> Gaps { get; init; } = [];

        /// <summary>
        /// An imported route's own points (T083): followed wherever the match doesn't cover them,
        /// before its first junction, after its last and across its gaps (user, 2026-11-08).
        /// </summary>
        public GpxPoints? Gpx { get; init; }

        // A GPX point this near a junction is where the route reaches it; the first such run of
        // points from where the route has got to is taken, not a later pass by the same place.
        private const double ReachesM = 50;

        // A run of the match ends where the GPX reaches its last junction once it has gone at least
        // this share of the run's length: a Loop's last junction is its first too, near the GPX's
        // start as well as its end (found 2026-11-08: a closed route's line went round twice).
        private const double RunAtLeast = 0.8;

        // A Loop for the map: an imported route's along the GPX where the match doesn't cover it,
        // starting where the GPX does; its gaps with it.
        private async Task<PlannedLoop> TracedAsync(Loop loop, CancellationToken cancellationToken)
        {
            var traced = await planner.ForTheMapAsync(slice, loop, cancellationToken) with { Gaps = Gaps };
            if (Gpx is not { } gpx || loop.Traversals.Count == 0) return traced;

            // The Loop in runs of Traversals that join up: a gap lies between two.
            var runs = new List<List<Traversal>>();
            int? reached = null;
            foreach (var traversal in loop.Traversals)
            {
                if (reached != StartOf(traversal)) runs.Add([]);
                runs[^1].Add(traversal);
                reached = EndOf(traversal);
            }

            var line = new List<LatLon>();
            var ways = new List<long>();
            void Follow(int first, int last)
            {
                for (var i = first; i <= last; i++)
                {
                    line.Add(gpx.Points[i]);
                    ways.Add(0);
                }
            }
            var along = new double[gpx.Grid.Count];
            for (var i = 1; i < along.Length; i++) along[i] = along[i - 1] + gpx.Grid[i].DistanceTo(gpx.Grid[i - 1]);
            var at = 0;
            foreach (var run in runs)
            {
                var joins = Reaching(gpx.Grid, slice.Node(StartOf(run[0])), at);
                Follow(at, joins);
                var (runLine, runWays) = await planner.Geometry.TraceWithWaysAsync([.. Corners.OnTheMap(slice, run)], cancellationToken);
                line.AddRange(runLine);
                ways.AddRange(runWays.Count == runLine.Count ? runWays : runLine.Select(_ => 0L));
                var gone = along[joins] + (RunAtLeast * LengthOf(run));
                var from = Array.FindIndex(along, joins, m => m >= gone);
                at = Reaching(gpx.Grid, slice.Node(EndOf(run[^1])), from < 0 ? along.Length - 1 : from);
            }
            Follow(at, gpx.Points.Count - 1);
            return traced with { Line = line, Ways = ways, Start = gpx.Points[0] };
        }

        private int StartOf(Traversal t) => t.Forward ? slice.Segments[t.Segment].From : slice.Segments[t.Segment].To;

        private int EndOf(Traversal t) => t.Forward ? slice.Segments[t.Segment].To : slice.Segments[t.Segment].From;

        // The GPX point from `from` on where the route reaches a junction: the nearest of the first
        // run within ReachesM of it, or the nearest of all if none is that near.
        private static int Reaching(IReadOnlyList<NodePoint> gpx, NodePoint junction, int from)
        {
            var (best, bestM) = (from, double.MaxValue);
            var near = false;
            for (var i = from; i < gpx.Count; i++)
            {
                var m = gpx[i].DistanceTo(junction);
                if (near && m > ReachesM) break;
                near |= m <= ReachesM;
                if (m < bestM) (best, bestM) = (i, m);
            }
            return best;
        }

        // How far from the route a Nudge may be put on it, or dropped from a junction to go through.
        private const double NearM = 500;

        private readonly ConcurrentDictionary<Loop, PlannedLoop> _traced = new(ReferenceEqualityComparer.Instance);
        private readonly ConcurrentDictionary<Loop, PlannedLoop> _inAnotherDirection = new(ReferenceEqualityComparer.Instance);

        // Each Loop handed out, found or nudged, and the Segments its Nudges have avoided (T076).
        private readonly ConcurrentDictionary<PlannedLoop, (Loop Loop, IReadOnlyList<int> Avoided)> _behind = new(ReferenceEqualityComparer.Instance);
        private readonly HashSet<Leans> _searched = request is null ? [] : [request.Leans];
        private FoundLoops? _found = found;

        /// <summary>The Request's own Loops, as found so far and at last (T054): what's traced is kept.</summary>
        public void Now(FoundLoops found)
        {
            lock (_searched) _found = found;
        }

        public async Task<IOfferedLoops> SearchAsync(Leans leans, Action<PlanningProgress>? progress, CancellationToken cancellationToken)
        {
            bool searched;
            lock (_searched) searched = _searched.Contains(leans);
            if (!searched)
            {
                var more = await planner.TryAsync(slice, request!.At(leans), progress, null, cancellationToken);
                lock (_searched)
                {
                    // Another search at this Lean may have finished first.
                    if (_searched.Add(leans)) _found = _found!.With(more);
                }
            }
            return await OfferAsync(leans, cancellationToken);
        }

        public async Task<IOfferedLoops> OfferAsync(Leans leans, CancellationToken cancellationToken)
        {
            FoundLoops found;
            lock (_searched) found = _found!;
            var loops = await Task.Run(() => found.Offer(leans), cancellationToken);
            var planned = new List<PlannedLoop>();
            for (var i = 0; i < loops.Count; i++)
            {
                var loop = loops[i];
                if (!_traced.TryGetValue(loop, out var traced))
                    traced = _traced.GetOrAdd(loop, await planner.ForTheMapAsync(slice, loop, cancellationToken));
                // The same Loop may be on the bar at one Lean and below it at another: marked once and
                // kept, so re-ranked it's the same Loop, not a copy (#149, T145: seen on the real map).
                if (loops.InAnotherDirection(i)) traced = _inAnotherDirection.GetOrAdd(loop, traced with { InAnotherDirection = true });
                _behind.TryAdd(traced, (loop, []));
                planned.Add(traced);
            }
            return new Offered(this, planned);
        }

        /// <summary>A Saved Route reopened (T085): itself, offered alone, its Loop and Avoids behind it.</summary>
        public Offered Reopened(PlannedLoop saved, Loop loop, IReadOnlyList<int> avoided)
        {
            _behind.TryAdd(saved, (loop, avoided));
            return new Offered(this, [saved], alone: true);
        }

        /// <summary>Point-to-points offered (T090): nothing to re-rank or search for yet.</summary>
        public async Task<IOfferedLoops> PointToPointsAsync(IReadOnlyList<Loop> loops, double shortestM, CancellationToken cancellationToken)
        {
            var planned = new List<PlannedLoop>();
            foreach (var loop in loops)
            {
                var traced = await planner.ForTheMapAsync(slice, loop, cancellationToken);
                _behind.TryAdd(traced, (loop, []));
                planned.Add(traced);
            }
            return new Offered(this, planned, alone: true) { ShortestM = shortestM };
        }

        /// <summary>An imported route offered alone (T083): nothing to re-rank or search for.</summary>
        public async Task<IOfferedLoops> ImportedAsync(Loop loop, CancellationToken cancellationToken)
        {
            var traced = await TracedAsync(loop, cancellationToken);
            _behind.TryAdd(traced, (loop, []));
            return new Offered(this, [traced], alone: true);
        }

        public async Task<NudgedLoop?> NudgeAsync(PlannedLoop planned, LoopNudge nudge, Leans leans, CancellationToken cancellationToken)
        {
            if (!_behind.TryGetValue(planned, out var behind)) return null;
            var weights = leans.On(planner.Weights.For(activity));
            var (loop, avoided) = behind;
            // The possible Map gaps it goes through (T146c) are the ones the user included: still usable.
            var included = loop.Traversals.Select(t => t.Segment).Where(s => slice.Segments[s].MapGap == MapGapKind.Possible).ToHashSet();
            Loop? nudged;
            switch (nudge)
            {
                case ViaNudge via:
                    var grabbed = Nearest(loop, await planner.Geometry.ToGridAsync(via.Grabbed, cancellationToken));
                    var to = await planner.Geometry.ToGridAsync(via.To, cancellationToken);
                    if (grabbed is null) return null;
                    var kept = avoided.ToHashSet();
                    // Dropped on a path (T126): along it, or at its end's junction if near one; with no
                    // path near, the nearest junction, as before. A drop doesn't say which side of a Main
                    // Road it means (T145c): each side's tried, and the cheapest kept.
                    if (await DroppedOnAsync(to, cancellationToken) is { } on)
                        nudged = await Task.Run(() => Cheapest(on.Along
                            ? on.Sides.Select(s => (Func<Loop?>)(() => Nudges.Through(slice, loop, grabbed.Value, s,
                                on.AtFrom ? slice.Segments[s].From : slice.Segments[s].To, weights, kept, included: included)))
                            : on.Sides.SelectMany(s => Corners.Of(slice, on.AtFrom ? slice.Segments[s].From : slice.Segments[s].To)).Distinct()
                                .Select(c => (Func<Loop?>)(() => Nudges.Via(slice, loop, grabbed.Value, c, weights, kept, included: included))), weights), cancellationToken);
                    else if (NearestJunction(to) is { } junction)
                        nudged = await Task.Run(() => Cheapest(Corners.Of(slice, junction)
                            .Select(c => (Func<Loop?>)(() => Nudges.Via(slice, loop, grabbed.Value, c, weights, kept, included: included))), weights), cancellationToken);
                    else
                        return null;
                    break;
                case AvoidNudge avoid:
                    if (Nearest(loop, await planner.Geometry.ToGridAsync(avoid.At, cancellationToken)) is not { } at) return null;
                    avoided = [.. avoided, loop.Traversals[at].Segment];
                    var off = avoided.ToHashSet();
                    nudged = await Task.Run(() => Nudges.Avoid(slice, loop, weights, off, included: included), cancellationToken);
                    break;
                case RemoveStopNudge stop when stop.Stop >= 0 && stop.Stop < loop.Anchors.Count:
                    var still = avoided.ToHashSet();
                    nudged = await Task.Run(() => Nudges.WithoutStop(slice, loop, stop.Stop, weights, still, included: included), cancellationToken);
                    break;
                case RemoveAvoidNudge removed when removed.Avoid >= 0 && removed.Avoid < avoided.Count:
                    // The stretch round where it was found again, the Segment usable again.
                    var near = NearestAnywhere(loop, MiddleOf(avoided[removed.Avoid]));
                    avoided = [.. avoided.Where((_, i) => i != removed.Avoid)];
                    var rest = avoided.ToHashSet();
                    nudged = await Task.Run(() => Nudges.Again(slice, loop, near, weights, rest, included: included), cancellationToken);
                    break;
                case IncludeNudge include:
                    // Through the possible gap clicked (T146c), from the end nearer the Loop, which stops there.
                    if (PossibleMapGapAt(await planner.Geometry.ToGridAsync(include.At, cancellationToken)) is not { } gap) return null;
                    var (gapFrom, gapTo) = (slice.Node(slice.Segments[gap].From), slice.Node(slice.Segments[gap].To));
                    var (fromAt, toAt) = (NearestAnywhere(loop, gapFrom), NearestAnywhere(loop, gapTo));
                    var meets = DistanceTo(loop, fromAt, gapFrom) <= DistanceTo(loop, toAt, gapTo);
                    var allowed = included.Append(gap).ToHashSet();
                    var notThese = avoided.ToHashSet();
                    nudged = await Task.Run(() => Nudges.Through(slice, loop, meets ? fromAt : toAt, gap,
                        meets ? slice.Segments[gap].From : slice.Segments[gap].To, weights, notThese, included: allowed), cancellationToken);
                    break;
                case RemoveStopNudge or RemoveAvoidNudge:
                    return null;
                default:
                    throw new ArgumentOutOfRangeException(nameof(nudge), nudge, "A Via, an Avoid, an Include or a removal.");
            }
            if (nudged is null) return null;
            var traced = await TracedAsync(nudged, cancellationToken);
            traced = traced with { Avoids = avoided.Count == 0 ? [] : await planner.Geometry.ToLatLonAsync([.. avoided.Select(MiddleOf)], cancellationToken) };
            _behind[traced] = (nudged, avoided);
            // An imported route asked for no length: no window to be outside.
            if (request is null) return new NudgedLoop(traced, 0);
            var km = LengthOf(nudged.Traversals) / 1000;
            return new NudgedLoop(traced, km > request.LongestKm ? km - request.LongestKm : km < request.ShortestKm ? km - request.ShortestKm : 0);
        }

        public IReadOnlyList<(SegmentKey Key, bool Forward)>? TraversalsOf(PlannedLoop planned) =>
            _behind.TryGetValue(planned, out var behind) ? [.. Corners.OnTheMap(slice, behind.Loop.Traversals)] : null;

        // On the map, all of it; an imported route, all but its gaps (T083).
        public RouteScore? ScoreOf(PlannedLoop planned)
        {
            if (!_behind.TryGetValue(planned, out var behind)) return null;
            var gapsM = planned.Gaps.Sum(g => g.LengthM);
            var share = planned.Breakdown.LengthM / (planned.Breakdown.LengthM + gapsM);
            // A Trail Ride, with no Outings of its own yet, is scored as a Gravel Ride is: its cut points are one's.
            var scoredAs = activity == Activity.TrailRide ? Activity.GravelRide : activity;
            return new RouteScore(scoredAs, LoopScorer.Score(slice, behind.Loop, ScoringWeights.Default.For(scoredAs)), share,
                planned.Breakdown.OffRoadShare, planned.Breakdown.ClimbPerKm);
        }

        public async Task<SegmentKey?> SegmentAtAsync(PlannedLoop planned, LatLon at, CancellationToken cancellationToken)
        {
            if (!_behind.TryGetValue(planned, out var behind)) return null;
            return Nearest(behind.Loop, await planner.Geometry.ToGridAsync(at, cancellationToken)) is { } t
                ? slice.Segments[behind.Loop.Traversals[t].Segment].Key
                : null;
        }

        // A Segment's middle, between its junctions.
        private NodePoint MiddleOf(int segment)
        {
            var (from, to) = (slice.Node(slice.Segments[segment].From), slice.Node(slice.Segments[segment].To));
            return new NodePoint((from.X + to.X) / 2, (from.Y + to.Y) / 2);
        }

        // The Traversal of the Loop nearest a point, however far.
        private int NearestAnywhere(Loop loop, NodePoint point)
        {
            var (best, bestM) = (0, double.MaxValue);
            for (var t = 0; t < loop.Traversals.Count; t++)
            {
                var segment = slice.Segments[loop.Traversals[t].Segment];
                if (segment.IsCrossingLink) continue;
                var m = FromChord(point, slice.Node(segment.From), slice.Node(segment.To));
                if (m < bestM) (best, bestM) = (t, m);
            }
            return best;
        }

        // How far a point is from a Traversal of the Loop.
        private double DistanceTo(Loop loop, int traversal, NodePoint point)
        {
            var segment = slice.Segments[loop.Traversals[traversal].Segment];
            return FromChord(point, slice.Node(segment.From), slice.Node(segment.To));
        }

        // How near a click a possible Map gap is to be the one clicked (T146c): its line is drawn thin.
        private const double ClickedGapWithinM = 25;

        // The possible Map gap clicked (T146c): the nearest to the point, if one is within ClickedGapWithinM of it.
        private int? PossibleMapGapAt(NodePoint point)
        {
            var (best, bestM) = (-1, double.MaxValue);
            for (var s = 0; s < slice.Segments.Count; s++)
            {
                var segment = slice.Segments[s];
                if (segment.MapGap != MapGapKind.Possible) continue;
                var m = FromChord(point, slice.Node(segment.From), slice.Node(segment.To));
                if (m < bestM) (best, bestM) = (s, m);
            }
            return best >= 0 && bestM <= ClickedGapWithinM ? best : null;
        }

        // The Traversal of the Loop nearest a point, if any is within NearM of it.
        private int? Nearest(Loop loop, NodePoint point)
        {
            var (best, bestM) = (-1, double.MaxValue);
            for (var t = 0; t < loop.Traversals.Count; t++)
            {
                var segment = slice.Segments[loop.Traversals[t].Segment];
                if (segment.IsCrossingLink) continue;
                var m = FromChord(point, slice.Node(segment.From), slice.Node(segment.To));
                if (m < bestM) (best, bestM) = (t, m);
            }
            return bestM <= NearM ? best : null;
        }

        // Of the Loops a Nudge may make, the one that costs least, as a Path does; none if none can be made.
        private Loop? Cheapest(IEnumerable<Func<Loop?>> tries, ScoringWeights weights)
        {
            var cost = new RunCostModel(weights);
            return tries.Select(t => t()).OfType<Loop>()
                .MinBy(l => l.Traversals.Sum(t => cost.CostOf(slice.Segments[t.Segment], t.Forward)));
        }

        // How long a stretch of a Loop is, as the map draws it: crossing a Main Road at a junction adds nothing (T145c).
        private double LengthOf(IEnumerable<Traversal> traversals) =>
            traversals.Select(t => slice.Segments[t.Segment]).Where(s => !s.IsCrossingLink).Sum(s => s.LengthM);

        // The junction a bike or a Run may reach nearest a point, if one is within NearM of it.
        // A drop this near a Segment's end is at the junction there, as a drop always was (T126).
        private const double AtEndM = 20;

        // How many Segments near a drop are looked at for one of this slice's that may be used.
        private const int SegmentsLookedAt = 10;

        private Dictionary<SegmentKey, int[]>? _indexOf;

        /// <summary>
        /// The Segment a route was dropped on (T126): the nearest of the slice's, by its line, that may
        /// be used, within <see cref="NearM"/>; its stop at its end nearer the drop, and whether the
        /// drop is along it (more than <see cref="AtEndM"/> from both ends) or at that end's junction.
        /// </summary>
        private async Task<(int[] Sides, bool AtFrom, bool Along)?> DroppedOnAsync(NodePoint at, CancellationToken cancellationToken)
        {
            _indexOf ??= slice.Segments.Select((s, i) => (s.Key, i)).GroupBy(p => p.Key).ToDictionary(g => g.Key, g => g.Select(p => p.i).ToArray());
            foreach (var near in await planner.Geometry.SegmentsNearAsync(at, NearM, SegmentsLookedAt, cancellationToken))
            {
                // A Main Road's carriageway is two Segments in a Run's slice, one each side (T145c).
                if (!_indexOf.TryGetValue(near.Key, out var sides) || slice.Segments[sides[0]].Access == AccessKind.Forbidden) continue;
                var fromStart = near.AlongM;
                var fromEnd = near.LengthM - near.AlongM;
                return (sides, fromStart <= fromEnd, Math.Min(fromStart, fromEnd) > AtEndM);
            }
            return null;
        }

        private int? NearestJunction(NodePoint point)
        {
            var (best, bestM) = (-1, double.MaxValue);
            for (var n = 0; n < slice.NodeCount; n++)
            {
                var m = slice.Node(n).DistanceTo(point);
                if (m >= bestM) continue;
                var usable = false;
                foreach (var s in slice.SegmentsAt(n)) usable |= slice.Segments[s].Access != AccessKind.Forbidden;
                if (usable) (best, bestM) = (n, m);
            }
            return bestM <= NearM ? best : null;
        }

        // How far a point is from the straight line between a Segment's ends.
        private static double FromChord(NodePoint p, NodePoint a, NodePoint b)
        {
            var (dx, dy) = (b.X - a.X, b.Y - a.Y);
            var along = (dx * dx) + (dy * dy) == 0 ? 0 : Math.Clamp((((p.X - a.X) * dx) + ((p.Y - a.Y) * dy)) / ((dx * dx) + (dy * dy)), 0, 1);
            return p.DistanceTo(new NodePoint(a.X + (along * dx), a.Y + (along * dy)));
        }
    }

    // The Loops offered; or an imported route alone (T083), which re-ranks and searches to itself.
    private sealed class Offered(Found found, List<PlannedLoop> loops, bool alone = false) : IOfferedLoops
    {
        public PlannedLoop this[int index] => loops[index];

        public double? ShortestM { get; init; }

        public int Count => loops.Count;

        public Task<IOfferedLoops> ReRankAsync(Leans leans, CancellationToken cancellationToken = default) =>
            alone ? Task.FromResult<IOfferedLoops>(this) : found.OfferAsync(leans, cancellationToken);

        public Task<IOfferedLoops> SearchAsync(Leans leans, Action<PlanningProgress>? progress = null, CancellationToken cancellationToken = default) =>
            alone ? Task.FromResult<IOfferedLoops>(this) : found.SearchAsync(leans, progress, cancellationToken);

        public Task<NudgedLoop?> NudgeAsync(PlannedLoop planned, LoopNudge nudge, Leans leans, CancellationToken cancellationToken = default) =>
            found.NudgeAsync(planned, nudge, leans, cancellationToken);

        public IReadOnlyList<(SegmentKey Key, bool Forward)>? TraversalsOf(PlannedLoop planned) => found.TraversalsOf(planned);

        public RouteScore? ScoreOf(PlannedLoop planned) => found.ScoreOf(planned);

        public Task<SegmentKey?> SegmentAtAsync(PlannedLoop planned, LatLon at, CancellationToken cancellationToken = default) =>
            found.SegmentAtAsync(planned, at, cancellationToken);

        public IEnumerator<PlannedLoop> GetEnumerator() => loops.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    // The planner's own weights, which a Nudge leans and makes its Activity's as a Request does;
    // and its map geometry, which puts a Nudge on the grid.
    private ScoringWeights Weights => weights;

    private MapGeometry Geometry => geometry;

    private async Task<PlannedLoop> ForTheMapAsync(NetworkSlice slice, Loop loop, CancellationToken cancellationToken)
    {
        (SegmentKey, bool)[] traversals = [.. Corners.OnTheMap(slice, loop.Traversals)];
        var (line, ways) = await geometry.TraceWithWaysAsync(traversals, cancellationToken);
        var points = await geometry.ToLatLonAsync([slice.Node(loop.Start), .. loop.Anchors.Select(slice.Node)], cancellationToken);
        // The Map gaps it crosses (T146b), each from where the Loop first meets it to where it leaves it: once,
        // though the Loop may cross back over it.
        var gaps = loop.Traversals.Where(t => slice.Segments[t.Segment].MapGap != MapGapKind.None).DistinctBy(t => t.Segment).ToArray();
        var ends = gaps.Length == 0 ? [] : await geometry.ToLatLonAsync([.. gaps.SelectMany(t =>
        {
            var segment = slice.Segments[t.Segment];
            return t.Forward ? new[] { slice.Node(segment.From), slice.Node(segment.To) } : [slice.Node(segment.To), slice.Node(segment.From)];
        })], cancellationToken);
        // The possible ones near it (T146c), from their end nearer the Loop, to include one this time.
        var possible = PossibleMapGapsNear(slice, loop);
        var possibleEnds = possible.Count == 0 ? [] : await geometry.ToLatLonAsync([.. possible.SelectMany(p => new[] { p.Near, p.Far })], cancellationToken);
        return new PlannedLoop(line, points[0], [.. points.Skip(1)], Breakdown.Of(slice, loop), await ProfileAsync(traversals, cancellationToken))
        {
            Ways = ways,
            MapGaps = [.. gaps.Select((t, i) => new MapGapCrossed(ends[2 * i], ends[(2 * i) + 1], slice.Segments[t.Segment].LengthM))],
            PossibleMapGaps = [.. possible.Select((p, i) => new MapGapCrossed(possibleEnds[2 * i], possibleEnds[(2 * i) + 1], p.LengthM))],
        };
    }

    /// <summary>How near a Loop's junctions a possible Map gap's end is to be drawn for it (T146c).</summary>
    public const double PossibleMapGapNearM = 150;

    // The possible Map gaps with an end within reach of a junction the Loop passes, and not on it.
    private static List<(int Segment, NodePoint Near, NodePoint Far, double LengthM)> PossibleMapGapsNear(NetworkSlice slice, Loop loop)
    {
        var found = new List<(int, NodePoint, NodePoint, double)>();
        if (loop.Traversals.Count == 0) return found;
        var on = loop.Traversals.Select(t => t.Segment).ToHashSet();
        NodePoint[]? junctions = null;
        for (var s = 0; s < slice.Segments.Count; s++)
        {
            var segment = slice.Segments[s];
            if (segment.MapGap != MapGapKind.Possible || on.Contains(s)) continue;
            junctions ??= [.. loop.Traversals.Select(t => slice.Node(slice.Segments[t.Segment].From)).Concat(loop.Traversals.Select(t => slice.Node(slice.Segments[t.Segment].To))).Distinct()];
            var (from, to) = (slice.Node(segment.From), slice.Node(segment.To));
            var (fromM, toM) = (junctions.Min(j => j.DistanceTo(from)), junctions.Min(j => j.DistanceTo(to)));
            if (Math.Min(fromM, toM) > PossibleMapGapNearM) continue;
            found.Add(fromM <= toM ? (s, from, to, segment.LengthM) : (s, to, from, segment.LengthM));
        }
        return found;
    }

    private async Task<IReadOnlyList<ProfileSample>> ProfileAsync((SegmentKey, bool)[] traversals, CancellationToken cancellationToken)
    {
        if (terrain is null) return [];
        var onGrid = await geometry.TraceOnGridAsync(traversals, cancellationToken);
        // The grid caches the tiles it reads, and Requests can overlap (a new one starts as the
        // last is cancelled): one at a time.
        IReadOnlyList<ProfilePoint> profile;
        lock (terrain) profile = ElevationProfile.Along(onGrid, terrain.Height);
        var at = await geometry.ToLatLonAsync([.. profile.Select(p => p.At)], cancellationToken);
        return [.. profile.Select((p, i) => new ProfileSample(p.DistanceM, p.HeightM, at[i]))];
    }
}
