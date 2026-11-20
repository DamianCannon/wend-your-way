using WendYourWay.Core.Loops;
using WendYourWay.Core.Network;

namespace WendYourWay.Core.Scoring;

/// <summary>
/// Flow (CONTEXT.md): how much of a Loop is in long unbroken stretches of green ground. The rule
/// was fixed before it was tried against the user's rated Loops (PLAN T033), from the Slice 2
/// analysis, where it alone separated every very good Loop from every good one.
/// </summary>
public static class Flow
{
    /// <summary>
    /// A green stretch this long or longer counts as Flow; an Off-road network this large draws
    /// Anchors, since it could hold one.
    /// </summary>
    public const double LongStretchM = 2000;

    /// <summary>
    /// The metres of a Loop in green stretches (<see cref="OffRoadNetworks.IsGreen"/>, bridging road
    /// links of up to <see cref="OffRoadNetworks.BridgedRoadM"/>) of <see cref="LongStretchM"/> or
    /// more, its ends joined.
    /// </summary>
    public static double Of(NetworkSlice slice, Loop loop) => GreenStretches(slice, loop).Select(s => s.Metres).Where(m => m >= LongStretchM).Sum();

    /// <summary>The Loop's longest green stretch, bridging short road links as Flow does.</summary>
    public static double Longest(NetworkSlice slice, Loop loop) => LongestStretch(slice, loop)?.Metres ?? 0;

    /// <summary>
    /// The Loop's longest green stretch and where it runs (T157), the first of equals; none if it has
    /// no green. The same stretch as <see cref="Longest"/>, so the map's label and the Breakdown agree.
    /// </summary>
    public static GreenStretch? LongestStretch(NetworkSlice slice, Loop loop)
    {
        GreenStretch? longest = null;
        foreach (var stretch in GreenStretches(slice, loop))
        {
            if (longest is null || stretch.Metres > longest.Metres) longest = stretch;
        }
        return longest;
    }

    // Every green stretch round the Loop, short road links bridged and its ends joined.
    private static List<GreenStretch> GreenStretches(NetworkSlice slice, Loop loop)
    {
        ArgumentNullException.ThrowIfNull(slice);
        ArgumentNullException.ThrowIfNull(loop);

        // Runs of green and road along the Loop, in order, with the Traversals they start and end on.
        // Ground the Loop has already covered counts as road, so coming back the same way breaks a
        // stretch rather than lengthening it (T037 review); a short retrace is bridged like a short
        // road link.
        var runs = new List<(bool Green, double Metres, int First, int Last)>();
        var covered = new HashSet<int>();
        for (var t = 0; t < loop.Traversals.Count; t++)
        {
            var segment = slice.Segments[loop.Traversals[t].Segment];
            var retraced = segment.RetracedMetres(covered);
            Add(OffRoadNetworks.IsGreen(segment), segment.LengthM - retraced, t);
            Add(false, retraced, t);
            segment.Cover(covered);
        }
        // The Loop comes back to its start, so its last run carries on into its first.
        if (runs.Count > 1 && runs[0].Green == runs[^1].Green)
        {
            runs[0] = (runs[0].Green, runs[0].Metres + runs[^1].Metres, runs[^1].First, runs[0].Last);
            runs.RemoveAt(runs.Count - 1);
        }
        if (runs.Count == 0) return [];
        if (runs.Count == 1) return runs[0].Green ? [new GreenStretch(runs[0].Metres, runs[0].First, runs[0].Last)] : [];

        // Short road links between green runs count as green; then join neighbouring green runs,
        // round the Loop, starting from a road run that stays road (if there is one).
        var bridged = runs.Select(r => (Green: r.Green || r.Metres <= OffRoadNetworks.BridgedRoadM, r.Metres, r.First, r.Last)).ToList();
        var first = bridged.FindIndex(r => !r.Green);
        if (first < 0) return [new GreenStretch(bridged.Sum(r => r.Metres), 0, loop.Traversals.Count - 1)];

        var stretches = new List<GreenStretch>();
        GreenStretch? stretch = null;
        for (var i = 1; i <= bridged.Count; i++)
        {
            var run = bridged[(first + i) % bridged.Count];
            if (run.Green)
            {
                stretch = stretch is null ? new GreenStretch(run.Metres, run.First, run.Last) : stretch with { Metres = stretch.Metres + run.Metres, Last = run.Last };
                continue;
            }
            if (stretch is { Metres: > 0 }) stretches.Add(stretch);
            stretch = null;
        }
        return stretches;

        void Add(bool green, double metres, int traversal)
        {
            if (metres <= 0) return;
            if (runs.Count > 0 && runs[^1].Green == green) runs[^1] = (green, runs[^1].Metres + metres, runs[^1].First, traversal);
            else runs.Add((green, metres, traversal, traversal));
        }
    }
}

/// <summary>
/// A green stretch of a Loop (T157): how long, and its first and last Traversals, by index; the last
/// before the first if it runs on round past the Loop's start.
/// </summary>
public sealed record GreenStretch(double Metres, int First, int Last);
