namespace WendYourWay.Core.Network;

/// <summary>A point on an elevation profile: how far along, how high, and where.</summary>
public sealed record ProfilePoint(double DistanceM, double HeightM, NodePoint At);

/// <summary>The ground's height along a Loop's line, for its elevation profile (PLAN T051, R3.5).</summary>
public static class ElevationProfile
{
    /// <summary>The longest gap between heights: OS Terrain 50's cell.</summary>
    public const double SampleEveryM = 50;

    /// <summary>
    /// The heights along <paramref name="line"/> by distance from its first point, with the
    /// ground's height at any point from <paramref name="height"/>.
    /// </summary>
    public static IReadOnlyList<ProfilePoint> Along(IReadOnlyList<NodePoint> line, Func<NodePoint, double> height)
    {
        ArgumentNullException.ThrowIfNull(line);
        ArgumentNullException.ThrowIfNull(height);
        var profile = new List<ProfilePoint> { new(0, height(line[0]), line[0]) };
        var distance = 0.0;
        for (var i = 1; i < line.Count; i++)
        {
            var (from, to) = (line[i - 1], line[i]);
            var length = from.DistanceTo(to);
            var samples = Math.Max(1, (int)Math.Ceiling(length / SampleEveryM));
            for (var s = 1; s <= samples; s++)
            {
                var along = (double)s / samples;
                var here = new NodePoint(from.X + ((to.X - from.X) * along), from.Y + ((to.Y - from.Y) * along));
                profile.Add(new ProfilePoint(distance + (length * along), height(here), here));
            }
            distance += length;
        }
        return profile;
    }
}
