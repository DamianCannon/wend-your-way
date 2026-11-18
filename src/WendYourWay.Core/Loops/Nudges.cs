using WendYourWay.Core.Network;
using WendYourWay.Core.Routing;
using WendYourWay.Core.Scoring;

namespace WendYourWay.Core.Loops;

/// <summary>
/// Nudges (PLAN T075; SPEC R9.3; CONTEXT.md: Nudge): a Loop changed where the user says, the rest of
/// it kept. Only a stretch of the Loop round the Nudge is found again: up to <see cref="ReachM"/>
/// along it either side, and never past one of its stops (the start, its Anchors, earlier Vias) nor
/// a quarter of its length, so a Loop with few stops keeps its shape. Found again as the Request's
/// Paths are, with the ground of the rest of the Loop counted as used, so it keeps clear of it.
/// </summary>
public static class Nudges
{
    /// <summary>
    /// How far along the Loop either side of a Nudge it may change (T075): a whole Path between
    /// stops can be 30 km, and a 40 km Loop with no Anchors shrank to 28 km when one was redone.
    /// </summary>
    public const double ReachM = 2000;

    /// <summary>
    /// The Loop through <paramref name="junction"/> as well, from the stretch round Traversal
    /// <paramref name="grabbed"/>, and a stop there; the Loop itself if the stretch already ends at
    /// it; none if no Path reaches it.
    /// </summary>
    /// <param name="weights">The Request's, for its Activity and at its Leans.</param>
    /// <param name="avoided">Segments an earlier Avoid took off the Loop, kept off.</param>
    public static Loop? Via(
        NetworkSlice slice, Loop loop, int grabbed, int junction, ScoringWeights weights, IReadOnlySet<int>? avoided = null, Landmarks? landmarks = null, IReadOnlySet<int>? included = null)
    {
        ArgumentNullException.ThrowIfNull(slice);
        ArgumentNullException.ThrowIfNull(loop);
        if (grabbed < 0 || grabbed >= loop.Traversals.Count) throw new ArgumentOutOfRangeException(nameof(grabbed), grabbed, "No Traversal of the Loop.");
        var stretch = StretchRound(slice, loop, grabbed);
        if (junction == stretch.From || junction == stretch.To) return loop;
        return Repaired(slice, loop, stretch, junction, weights, avoided ?? new HashSet<int>(), landmarks, included);
    }

    /// <summary>
    /// The Loop along Segment <paramref name="segment"/> as well (T126: a route dragged onto the
    /// middle of a path, which may have no junction for a long way), from the stretch round Traversal
    /// <paramref name="grabbed"/>: the whole Segment, in whichever direction costs less, and a stop at
    /// <paramref name="stop"/>, one of its ends. The Loop itself if the stretch already goes along it;
    /// none if it's avoided, can't be used, or no Path reaches it.
    /// </summary>
    public static Loop? Through(
        NetworkSlice slice, Loop loop, int grabbed, int segment, int stop, ScoringWeights weights, IReadOnlySet<int>? avoided = null, Landmarks? landmarks = null, IReadOnlySet<int>? included = null)
    {
        ArgumentNullException.ThrowIfNull(slice);
        ArgumentNullException.ThrowIfNull(loop);
        if (grabbed < 0 || grabbed >= loop.Traversals.Count) throw new ArgumentOutOfRangeException(nameof(grabbed), grabbed, "No Traversal of the Loop.");
        if (segment < 0 || segment >= slice.Segments.Count) throw new ArgumentOutOfRangeException(nameof(segment), segment, "No Segment of the slice.");
        var along = slice.Segments[segment];
        if (stop != along.From && stop != along.To) throw new ArgumentOutOfRangeException(nameof(stop), stop, "The stop is one of the Segment's ends.");
        // An avoided Segment costs no less than impassable, so it's refused where its cost is asked.
        avoided ??= new HashSet<int>();
        var stretch = StretchRound(slice, loop, grabbed);
        for (var t = stretch.First; t < stretch.End; t++)
        {
            if (loop.Traversals[t].Segment == segment) return loop;
        }
        // Along it, not out along it and back: the ways to and from it keep off it, unless it's a dead
        // end, where back along it is the only way.
        foreach (var alongOnce in new[] { true, false })
        {
            var (best, bestCost) = ((Loop?)null, double.PositiveInfinity);
            foreach (var forward in new[] { true, false })
            {
                if (RepairedThrough(slice, loop, stretch, new Traversal(segment, forward), stop, weights, avoided, alongOnce, landmarks, included) is { } way && way.Cost < bestCost)
                    (best, bestCost) = (way.Loop, way.Cost);
            }
            if (best is not null) return best;
        }
        return null;
    }

    // The stretch found again to the Segment's near end, along it, and on from its far end, with the
    // stop added; and what that costs. None if the Segment can't be used or a Path can't be found.
    private static (Loop Loop, double Cost)? RepairedThrough(
        NetworkSlice slice, Loop loop, Stretch stretch, Traversal through, int stop, ScoringWeights weights, IReadOnlySet<int> avoided, bool alongOnce, Landmarks? landmarks, IReadOnlySet<int>? included)
    {
        var used = new HashSet<int>();
        for (var t = 0; t < loop.Traversals.Count; t++)
        {
            if (t < stretch.First || t >= stretch.End) slice.Segments[loop.Traversals[t].Segment].Cover(used);
        }
        var off = new HashSet<SegmentKey>(avoided.Select(s => slice.Segments[s].Key));
        var allowed = Allowed(slice, included);
        var cost = new Avoiding(new RunCostModel(weights, used), off, allowed);
        var along = slice.Segments[through.Segment];
        // The ways to and from it, kept off it when it's to be gone along once.
        var legCost = alongOnce ? new Avoiding(new RunCostModel(weights, used), [.. off, along.Key], allowed) : cost;
        var search = new PathSearch(slice);
        var (entry, exit) = through.Forward ? (along.From, along.To) : (along.To, along.From);
        var found = new List<Traversal>();
        var total = 0.0;

        bool Leg(int from, int to)
        {
            if (from == to) return true;
            var path = search.Find(PathEnd.AtNode(from), PathEnd.AtNode(to), legCost, landmarks);
            if (path is null) return false;
            found.AddRange(path.Traversals);
            total += path.Cost;
            foreach (var t in path.Traversals) slice.Segments[t.Segment].Cover(used);
            return true;
        }

        if (!Leg(stretch.From, entry)) return null;
        var alongCost = cost.CostOf(along, through.Forward);
        if (double.IsInfinity(alongCost) || double.IsNaN(alongCost)) return null;
        found.Add(through);
        total += alongCost;
        along.Cover(used);
        if (!Leg(exit, stretch.To)) return null;
        return (new Loop(
            loop.Start,
            [.. loop.Anchors.Take(stretch.AnchorsBefore), stop, .. loop.Anchors.Skip(stretch.AnchorsBefore)],
            [.. loop.Traversals.Take(stretch.First), .. found, .. loop.Traversals.Skip(stretch.End)]), total);
    }

    /// <summary>
    /// The Loop with every Segment in <paramref name="avoided"/> off it, the stretch round each
    /// found again without them; the Loop itself if none is on it; none if a stretch can't go round.
    /// </summary>
    public static Loop? Avoid(NetworkSlice slice, Loop loop, ScoringWeights weights, IReadOnlySet<int> avoided, Landmarks? landmarks = null, IReadOnlySet<int>? included = null)
    {
        ArgumentNullException.ThrowIfNull(slice);
        ArgumentNullException.ThrowIfNull(loop);
        ArgumentNullException.ThrowIfNull(avoided);
        // One at a time: a stretch found again may take another off with it.
        for (var tries = 0; tries <= avoided.Count; tries++)
        {
            var on = loop.Traversals.ToList().FindIndex(t => avoided.Contains(t.Segment));
            if (on < 0) return loop;
            if (Repaired(slice, loop, StretchRound(slice, loop, on), via: null, weights, avoided, landmarks, included) is not { } repaired) return null;
            loop = repaired;
        }
        return loop;
    }

    /// <summary>
    /// The Loop without its stop <paramref name="stop"/> (an Anchor or a Via, by its place among
    /// them), the stretch round it found again not through it (T079); none if there's no way.
    /// </summary>
    public static Loop? WithoutStop(
        NetworkSlice slice, Loop loop, int stop, ScoringWeights weights, IReadOnlySet<int>? avoided = null, Landmarks? landmarks = null, IReadOnlySet<int>? included = null)
    {
        ArgumentNullException.ThrowIfNull(slice);
        ArgumentNullException.ThrowIfNull(loop);
        if (stop < 0 || stop >= loop.Anchors.Count) throw new ArgumentOutOfRangeException(nameof(stop), stop, "No such stop.");
        var at = Reaching(slice, loop, stop);
        var without = loop with { Anchors = [.. loop.Anchors.Where((_, i) => i != stop)] };
        return at < 0 ? without : Repaired(slice, without, StretchRound(slice, without, at), via: null, weights, avoided ?? new HashSet<int>(), landmarks, included);
    }

    /// <summary>
    /// The stretch round Traversal <paramref name="at"/> found again with only
    /// <paramref name="avoided"/> avoided: an Avoid removed, its Segment usable again (T079).
    /// </summary>
    public static Loop? Again(NetworkSlice slice, Loop loop, int at, ScoringWeights weights, IReadOnlySet<int> avoided, Landmarks? landmarks = null, IReadOnlySet<int>? included = null)
    {
        ArgumentNullException.ThrowIfNull(slice);
        ArgumentNullException.ThrowIfNull(loop);
        ArgumentNullException.ThrowIfNull(avoided);
        if (at < 0 || at >= loop.Traversals.Count) throw new ArgumentOutOfRangeException(nameof(at), at, "No Traversal of the Loop.");
        return Repaired(slice, loop, StretchRound(slice, loop, at), via: null, weights, avoided, landmarks, included);
    }

    // The Traversal that reaches stop number `stop` in its turn, or −1.
    private static int Reaching(NetworkSlice slice, Loop loop, int stop)
    {
        var next = 0;
        for (var t = 0; t < loop.Traversals.Count; t++)
        {
            var segment = slice.Segments[loop.Traversals[t].Segment];
            if ((loop.Traversals[t].Forward ? segment.To : segment.From) != loop.Anchors[next]) continue;
            if (next == stop) return t;
            if (++next == loop.Anchors.Count) break;
        }
        return -1;
    }

    // A stretch of the Loop: Traversals First up to End (not included), from junction From to To,
    // and how many of the Loop's Anchors come before it.
    private sealed record Stretch(int First, int End, int From, int To, int AnchorsBefore);

    // The stretch round Traversal at: out from it either way until ReachM (or a quarter of the Loop)
    // is passed, or a stop is reached.
    private static Stretch StretchRound(NetworkSlice slice, Loop loop, int at)
    {
        int StartOf(int t) => slice.Segments[loop.Traversals[t].Segment] is var s && loop.Traversals[t].Forward ? s.From : s.To;
        int EndOf(int t) => slice.Segments[loop.Traversals[t].Segment] is var s && loop.Traversals[t].Forward ? s.To : s.From;
        double LengthOf(int t) => slice.Segments[loop.Traversals[t].Segment].LengthM;

        // The stops, each at the Traversal it ends: the Anchors in turn, where the Loop reaches them.
        var stopAfter = new bool[loop.Traversals.Count];
        var (next, before) = (0, 0);
        for (var t = 0; t < loop.Traversals.Count && next < loop.Anchors.Count; t++)
        {
            if (EndOf(t) != loop.Anchors[next]) continue;
            stopAfter[t] = true;
            if (t < at) before = next + 1;
            next++;
        }

        var reach = Math.Min(ReachM, loop.Traversals.Sum(t => slice.Segments[t.Segment].LengthM) / 4);
        var first = at;
        for (var back = 0.0; first > 0 && !stopAfter[first - 1] && back < reach; first--) back += LengthOf(first - 1);
        var end = at + 1;
        for (var on = 0.0; end < loop.Traversals.Count && !stopAfter[end - 1] && on < reach; end++) on += LengthOf(end);
        return new Stretch(first, end, StartOf(first), EndOf(end - 1), before);
    }

    // The stretch found again, through the Via if there is one, the rest of the Loop kept and its
    // ground counted as used; the Via a stop among the Anchors.
    private static Loop? Repaired(NetworkSlice slice, Loop loop, Stretch stretch, int? via, ScoringWeights weights, IReadOnlySet<int> avoided, Landmarks? landmarks, IReadOnlySet<int>? included = null)
    {
        var used = new HashSet<int>();
        for (var t = 0; t < loop.Traversals.Count; t++)
        {
            if (t < stretch.First || t >= stretch.End) slice.Segments[loop.Traversals[t].Segment].Cover(used);
        }
        var off = new HashSet<SegmentKey>(avoided.Select(s => slice.Segments[s].Key));
        var search = new PathSearch(slice);
        var found = new List<Traversal>();
        int[] stops = via is { } v ? [stretch.From, v, stretch.To] : [stretch.From, stretch.To];
        for (var i = 0; i + 1 < stops.Length; i++)
        {
            var path = search.Find(PathEnd.AtNode(stops[i]), PathEnd.AtNode(stops[i + 1]), new Avoiding(new RunCostModel(weights, used), off, Allowed(slice, included)), landmarks);
            if (path is null) return null;
            found.AddRange(path.Traversals);
            foreach (var t in path.Traversals) slice.Segments[t.Segment].Cover(used);
        }
        return new Loop(
            loop.Start,
            via is { } stop ? [.. loop.Anchors.Take(stretch.AnchorsBefore), stop, .. loop.Anchors.Skip(stretch.AnchorsBefore)] : loop.Anchors,
            [.. loop.Traversals.Take(stretch.First), .. found, .. loop.Traversals.Skip(stretch.End)]);
    }

    // The possible Map gaps the user included (T146c), by OSM identity.
    private static HashSet<SegmentKey>? Allowed(NetworkSlice slice, IReadOnlySet<int>? included) =>
        included is null || included.Count == 0 ? null : [.. included.Select(s => slice.Segments[s].Key)];

    // A cost model with the Segments avoided impassable: costs only rise, so Landmarks still hold. A
    // possible Map gap the user included (T146c) costs as a gap may, though forbidden to every search:
    // its few metres are the one cost that falls, and a Nudge goes through it on purpose.
    private sealed class Avoiding(ICostModel cost, HashSet<SegmentKey> avoided, HashSet<SegmentKey>? included = null) : ICostModel
    {
        public double FloorPerMetre => cost.FloorPerMetre;

        public double CostOf(Segment segment, bool forward) =>
            avoided.Count > 0 && avoided.Contains(segment.Key) ? double.PositiveInfinity
            : included is not null && included.Contains(segment.Key) ? cost.CostOf(segment with { Access = AccessKind.Allowed }, forward)
            : cost.CostOf(segment, forward);
    }
}
