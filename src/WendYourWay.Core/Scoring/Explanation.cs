using WendYourWay.Core.Loops;
using WendYourWay.Core.Network;

namespace WendYourWay.Core.Scoring;

/// <summary>What a note on a Loop says (T157): a Crossing, an Obstacle, a stretch of road, or its longest green stretch.</summary>
public enum NoteKind
{
    Crossing,
    Obstacle,
    Road,
    Green,
    /// <summary>A stretch on ways for customers only (T182): check you may pass.</summary>
    Customers,
}

/// <summary>Where on its Traversal a note is: where it starts, halfway along, or where it ends.</summary>
public enum NotePlace
{
    Start,
    Middle,
    End,
}

/// <summary>
/// A note on a Loop (T157): what it says and where, at a Traversal of the Loop, by index. Only what
/// the Network knows of it.
/// </summary>
public sealed record LoopNote(NoteKind Kind, int Traversal, NotePlace Place)
{
    /// <summary>Halfway along, how far along its Traversal, 0 to 1: a stretch's halfway is rarely its Segment's.</summary>
    public double Along { get; init; } = 0.5;

    /// <summary>A stretch's length: of road, or of green.</summary>
    public double LengthM { get; init; }

    /// <summary>Of a stretch of road, its metres on a Main Road.</summary>
    public double MainRoadM { get; init; }

    /// <summary>Of a stretch of road, whether its Main Road is all pavement beside one, not its carriageway (T145).</summary>
    public bool Pavement { get; init; }

    /// <summary>A Crossing's kind.</summary>
    public CrossingKind? Crossing { get; init; }

    /// <summary>Whether a Crossing is of a Main Road.</summary>
    public bool MainRoad { get; init; }

    /// <summary>An Obstacle's kind.</summary>
    public ObstacleKind? Obstacle { get; init; }
}

/// <summary>
/// Why a Loop goes the way it does (T157, #161; user: "I actually like the map labels in their own
/// right"): its Crossings and Obstacles, counted as the <see cref="Breakdown"/> counts them; its
/// stretches of road worth a word; and its longest green stretch, the Breakdown's. In the order it
/// goes.
/// </summary>
public static class Explanation
{
    /// <summary>A stretch of road this long is noted.</summary>
    public const double RoadNoteM = 200;

    /// <summary>
    /// A shorter one with this much Main Road is too: it's what the user looks for. Not a few metres of one,
    /// though (found trying it: "18 m of road, all beside a Main Road").
    /// </summary>
    public const double MainRoadNoteM = 150;

    public static IReadOnlyList<LoopNote> Of(NetworkSlice slice, Loop loop)
    {
        ArgumentNullException.ThrowIfNull(slice);
        ArgumentNullException.ThrowIfNull(loop);
        var traversals = loop.Traversals;
        var notes = new List<LoopNote>();
        Segment SegmentOf(int t) => slice.Segments[traversals[t].Segment];
        int EndOf(int t) => traversals[t].Forward ? SegmentOf(t).To : SegmentOf(t).From;

        // Half an Obstacle on each side of a junction is one, at the junction; round the start too if
        // the Loop ends where it began.
        var closed = traversals.Count > 1 && EndOf(traversals.Count - 1) == loop.Start;
        var paired = new HashSet<(int Traversal, ObstacleKind Kind)>();
        int? Next(int t) => t + 1 < traversals.Count ? t + 1 : closed ? 0 : null;
        bool HalfOf(int t, ObstacleKind kind) => (SegmentOf(t).Obstacles ?? []).Any(o => o.Kind == kind && o.Share < 1 - 1e-9);

        long? onCrossing = null;
        (int First, double Metres, double MainRoadM, bool Carriageway)? road = null;
        (int First, double Metres)? customers = null;
        for (var t = 0; t < traversals.Count; t++)
        {
            var segment = SegmentOf(t);

            // A Main Road crossed at a junction (T145): a Crossing, with no length of its own.
            if (segment.IsCrossingLink)
            {
                notes.Add(new LoopNote(NoteKind.Crossing, t, NotePlace.Middle) { Crossing = segment.Crossing ?? CrossingKind.Unmarked, MainRoad = true });
                onCrossing = null;
                continue;
            }

            // A crossing way cut into Segments is one Crossing: noted where its first Segment ends, mid-road, if it goes on.
            if (segment.Crossing is null) onCrossing = null;
            else if (onCrossing != segment.Key.OsmWayId)
            {
                onCrossing = segment.Key.OsmWayId;
                var goesOn = t + 1 < traversals.Count && SegmentOf(t + 1).Key.OsmWayId == segment.Key.OsmWayId;
                notes.Add(new LoopNote(NoteKind.Crossing, t, goesOn ? NotePlace.End : NotePlace.Middle) { Crossing = segment.Crossing, MainRoad = segment.CrossesMainRoad });
            }

            foreach (var obstacle in segment.Obstacles ?? [])
            {
                if (obstacle.Share >= 1 - 1e-9) notes.Add(new LoopNote(NoteKind.Obstacle, t, NotePlace.Middle) { Obstacle = obstacle.Kind });
                else if (paired.Contains((t, obstacle.Kind))) continue;
                else if (Next(t) is { } next && next != t && HalfOf(next, obstacle.Kind))
                {
                    paired.Add((next, obstacle.Kind));
                    notes.Add(new LoopNote(NoteKind.Obstacle, t, NotePlace.End) { Obstacle = obstacle.Kind });
                }
                else notes.Add(new LoopNote(NoteKind.Obstacle, t, NotePlace.Middle) { Obstacle = obstacle.Kind });
            }

            if (segment.Ground == GroundKind.Connector)
            {
                var (first, metres, mainRoad, carriageway) = road ?? (t, 0, 0, false);
                road = (first, metres + segment.LengthM, mainRoad + (segment.MainRoad ? segment.LengthM : 0), carriageway || (segment.MainRoad && segment.Carriageway));
            }
            else EndRoad(t - 1);

            // Ways for customers only (T182): a stretch of them, said once halfway along, with its length.
            if (segment.Customers) customers = (customers?.First ?? t, (customers?.Metres ?? 0) + segment.LengthM);
            else EndCustomers(t - 1);
        }
        EndRoad(traversals.Count - 1);
        EndCustomers(traversals.Count - 1);

        if (Flow.LongestStretch(slice, loop) is { Metres: > 0 } green)
        {
            var (t, along) = Halfway(green.First, green.Last);
            notes.Add(new LoopNote(NoteKind.Green, t, NotePlace.Middle) { Along = along, LengthM = green.Metres });
        }
        return [.. notes.OrderBy(n => n.Traversal)];

        void EndCustomers(int last)
        {
            if (customers is not { } c) return;
            customers = null;
            var (t, along) = Halfway(c.First, last);
            notes.Add(new LoopNote(NoteKind.Customers, t, NotePlace.Middle) { Along = along, LengthM = c.Metres });
        }

        void EndRoad(int last)
        {
            if (road is not { } r) return;
            road = null;
            if (r.Metres < RoadNoteM && r.MainRoadM < MainRoadNoteM) return;
            var (t, along) = Halfway(r.First, last);
            notes.Add(new LoopNote(NoteKind.Road, t, NotePlace.Middle)
                { Along = along, LengthM = r.Metres, MainRoadM = r.MainRoadM, Pavement = r.MainRoadM > 0 && !r.Carriageway });
        }

        // The Traversal halfway along from one to another, round past the start if need be, and how far along it.
        (int Traversal, double Along) Halfway(int first, int last)
        {
            var span = new List<int>();
            for (var t = first; ; t = (t + 1) % traversals.Count)
            {
                span.Add(t);
                if (t == last || span.Count == traversals.Count) break;
            }
            var half = span.Sum(t => SegmentOf(t).LengthM) / 2;
            var along = 0.0;
            foreach (var t in span)
            {
                var length = SegmentOf(t).LengthM;
                if (along + length >= half) return (t, length > 0 ? (half - along) / length : 0.5);
                along += length;
            }
            return (last, 0.5);
        }
    }
}
