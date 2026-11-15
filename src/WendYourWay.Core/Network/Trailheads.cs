namespace WendYourWay.Core.Network;

/// <summary>
/// A car park that might be a Trailhead (CONTEXT.md; PLAN T023): open to the public, beside an
/// Off-road network of at least <see cref="Trailheads.SmallestNetworkM"/>.
/// </summary>
/// <param name="OsmId">The car park's OSM id, an area's or a node's.</param>
/// <param name="At">Where it is: an area's point on its surface.</param>
/// <param name="Name">Its name, if it has one.</param>
/// <param name="AreaM2">Its area; 0 for a car park mapped as a point.</param>
/// <param name="Network">The Off-road network it's beside: the largest, if more than one.</param>
/// <param name="NetworkM">That network's metres of green ground.</param>
public sealed record TrailheadCandidate(long OsmId, NodePoint At, string? Name, double AreaM2, int Network, double NetworkM);

/// <summary>
/// A Trailhead in view (PLAN T060): its network, that network's green ground, how far it is from
/// the middle of the map, and the place it's named for, if any.
/// </summary>
public sealed record NearTrailhead(int Id, int Network, double NetworkM, double FromMiddleM, string? Place);

/// <summary>Which car parks are Trailheads (PLAN T023), and which to start from (T060).</summary>
public static class Trailheads
{
    /// <summary>How many places to start are offered: "ten might be better" (user, 2026-10-29).</summary>
    public const int Offered = 10;

    /// <summary>How many times the Distance of green ground is plenty: more is no better.</summary>
    public const double PlentyOfGreen = 3;

    /// <summary>
    /// The best car parks around here (T060), for a Loop of <paramref name="distanceKm"/>: of each
    /// place the one nearest the middle of the map, an unnamed car park its network's own place,
    /// the rest left to the map ("we
    /// just want one entry in the list and we could choose differently on the map", user,
    /// 2026-11-02); ranked by green ground up to <see cref="PlentyOfGreen"/> times the Distance,
    /// then by nearness to the middle; <see cref="Offered"/> of them, best first.
    /// </summary>
    public static IReadOnlyList<NearTrailhead> Around(IEnumerable<NearTrailhead> inView, double distanceKm)
    {
        ArgumentNullException.ThrowIfNull(inView);
        var plenty = PlentyOfGreen * distanceKm * 1000;
        return [.. inView
            .GroupBy(t => t.Place is null ? (t.Network, null) : (0, t.Place))
            .Select(place => place.MinBy(t => t.FromMiddleM)!)
            .OrderByDescending(t => Math.Min(t.NetworkM, plenty))
            .ThenBy(t => t.FromMiddleM)
            .ThenBy(t => t.Id)
            .Take(Offered)];
    }

    /// <summary>The least green ground a network needs for its car parks to be Trailheads: a 10 km Loop's worth.</summary>
    public const double SmallestNetworkM = 10_000;

    /// <summary>How near a green Segment of the network a car park must be.</summary>
    public const double BesideM = 100;

    /// <summary>
    /// How near a large green area (a park, common, nature reserve, wood, forest, heath, meadow,
    /// grassland or scrub) a car park must be: a shop's car park beside a path isn't a Trailhead
    /// ("Warren Evans car park", 2026-10-28). Fitted on London's Trailheads by name, not fixed first.
    /// </summary>
    public const double BesideGreenAreaM = 100;

    /// <summary>How large that green area must be: 5 hectares.</summary>
    public const double GreenAreaM2 = 50_000;

    /// <summary>
    /// How far apart two Trailheads on one network must be: nearer, they're one place to start.
    /// 1 km kept 23 of Epping Forest's 82 car parks beside its paths; 250 m keeps 54 ("include
    /// them all", user, 2026-11-02).
    /// </summary>
    public const double ApartM = 250;

    /// <summary>
    /// The car parks kept as Trailheads: none within <see cref="ApartM"/> of another kept on the
    /// same network, the named and then the larger chosen first.
    /// </summary>
    public static IReadOnlyList<TrailheadCandidate> Thin(IEnumerable<TrailheadCandidate> candidates)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        var kept = new List<TrailheadCandidate>();
        var byNetwork = new Dictionary<int, List<NodePoint>>();
        foreach (var candidate in candidates
            .OrderByDescending(c => c.Name is not null)
            .ThenByDescending(c => c.AreaM2)
            .ThenBy(c => c.OsmId))
        {
            if (!byNetwork.TryGetValue(candidate.Network, out var places)) byNetwork[candidate.Network] = places = [];
            if (places.Any(p => p.DistanceTo(candidate.At) < ApartM)) continue;
            places.Add(candidate.At);
            kept.Add(candidate);
        }
        return kept;
    }
}
