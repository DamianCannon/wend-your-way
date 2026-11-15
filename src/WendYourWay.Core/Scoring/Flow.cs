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
    public static double Of(NetworkSlice slice, Loop loop) => GreenStretches(slice, loop).Where(m => m >= LongStretchM).Sum();

    /// <summary>The Loop's longest green stretch, bridging short road links as Flow does.</summary>
    public static double Longest(NetworkSlice slice, Loop loop) => GreenStretches(slice, loop).DefaultIfEmpty(0).Max();

    // Every green stretch round the Loop, short road links bridged and its ends joined.
    private static List<double> GreenStretches(NetworkSlice slice, Loop loop)
    {
        ArgumentNullException.ThrowIfNull(slice);
        ArgumentNullException.ThrowIfNull(loop);

        // Runs of green and road along the Loop, in order. Ground the Loop has already covered
        // counts as road, so coming back the same way breaks a stretch rather than lengthening it
        // (T037 review); a short retrace is bridged like a short road link.
        var runs = new List<(bool Green, double Metres)>();
        var covered = new HashSet<int>();
        foreach (var traversal in loop.Traversals)
        {
            var segment = slice.Segments[traversal.Segment];
            var retraced = segment.RetracedMetres(covered);
            Add(OffRoadNetworks.IsGreen(segment), segment.LengthM - retraced);
            Add(false, retraced);
            segment.Cover(covered);
        }
        // The Loop comes back to its start, so its last run carries on into its first.
        if (runs.Count > 1 && runs[0].Green == runs[^1].Green)
        {
            runs[0] = (runs[0].Green, runs[0].Metres + runs[^1].Metres);
            runs.RemoveAt(runs.Count - 1);
        }
        if (runs.Count == 0) return [];
        if (runs.Count == 1) return runs[0].Green ? [runs[0].Metres] : [];

        // Short road links between green runs count as green; then join neighbouring green runs,
        // round the Loop, starting from a road run that stays road (if there is one).
        var bridged = runs.Select(r => (Green: r.Green || r.Metres <= OffRoadNetworks.BridgedRoadM, r.Metres)).ToList();
        var first = bridged.FindIndex(r => !r.Green);
        if (first < 0) return [bridged.Sum(r => r.Metres)];

        var stretches = new List<double>();
        var stretch = 0.0;
        for (var i = 1; i <= bridged.Count; i++)
        {
            var run = bridged[(first + i) % bridged.Count];
            if (run.Green)
            {
                stretch += run.Metres;
                continue;
            }
            if (stretch > 0) stretches.Add(stretch);
            stretch = 0;
        }
        return stretches;

        void Add(bool green, double metres)
        {
            if (metres <= 0) return;
            if (runs.Count > 0 && runs[^1].Green == green) runs[^1] = (green, runs[^1].Metres + metres);
            else runs.Add((green, metres));
        }
    }
}
