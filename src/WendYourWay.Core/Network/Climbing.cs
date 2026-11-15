namespace WendYourWay.Core.Network;

/// <summary>A Segment's climbing from the ground's height along it (PLAN T049).</summary>
public static class Climbing
{
    /// <summary>
    /// The longest gap between heights: closer misses less of a short rise, but the 50 m grid
    /// has no more to give (summed this way along the user's Strava runs, 1.03 times their gain).
    /// </summary>
    public const double SampleEveryM = 70;

    /// <summary>
    /// The metres climbed and descended going along <paramref name="line"/> from its first point,
    /// with the ground's height at any point from <paramref name="height"/>. A
    /// <paramref name="level"/> line (a bridge or tunnel, whose deck isn't the ground) climbs only
    /// from one end to the other.
    /// </summary>
    public static (double AscentM, double DescentM) Along(IReadOnlyList<NodePoint> line, bool level, Func<NodePoint, double> height)
    {
        ArgumentNullException.ThrowIfNull(line);
        ArgumentNullException.ThrowIfNull(height);
        if (level)
        {
            var rise = height(line[^1]) - height(line[0]);
            return (Math.Max(0, rise), Math.Max(0, -rise));
        }

        double ascent = 0, descent = 0, last = height(line[0]);
        for (var i = 1; i < line.Count; i++)
        {
            var (from, to) = (line[i - 1], line[i]);
            var samples = Math.Max(1, (int)Math.Ceiling(from.DistanceTo(to) / SampleEveryM));
            for (var s = 1; s <= samples; s++)
            {
                var along = (double)s / samples;
                var here = height(new NodePoint(from.X + ((to.X - from.X) * along), from.Y + ((to.Y - from.Y) * along)));
                if (here > last) ascent += here - last;
                else descent += last - here;
                last = here;
            }
        }
        return (ascent, descent);
    }
}
