using WendYourWay.Core.Loops;
using WendYourWay.Core.Network;

namespace WendYourWay.Core.Scoring;

/// <summary>
/// What a Loop is made of, shown with it so its score can be trusted (CONTEXT.md). This is the
/// basic Breakdown of STORY-029, Retrace and Flow (T033), with what the increments have added since.
/// Ground kinds don't overlap: Off-road, Traffic-free and Connector lengths add up to the whole.
/// </summary>
/// <param name="RetraceLengthM">
/// Length covering ground the Loop has already covered (R2.4), Stem included until Stems are
/// told apart (R2.5).
/// </param>
/// <param name="FlowM">Length in long green stretches, which raises the score (<see cref="Flow"/>).</param>
/// <param name="LongestGreenM">The longest green stretch, short road links bridged.</param>
/// <param name="MainRoadLengthM">Length on Main Roads (R2.1): only short links, if any.</param>
/// <param name="GreeneryPercent">How much of it lies beside green areas, by length (T036).</param>
/// <param name="CorridorLengthM">Length on Corridors (T037).</param>
/// <param name="MainCorridor">The Corridor it follows longest, or none.</param>
/// <param name="MainCorridorM">Length on <paramref name="MainCorridor"/>.</param>
/// <param name="OpenGroundM">Length straight across Open ground (T039), part of its Off-road.</param>
/// <param name="WatersidePercent">How much of it lies beside water, by length (T043).</param>
/// <param name="Crossings">How many times it crosses a road by a crossing way (T044).</param>
/// <param name="MainRoadCrossings">How many of those Crossings are of Main Roads.</param>
/// <param name="ClimbM">Its climbing, the metres of ascent going round (T049).</param>
/// <param name="DescentM">The metres of descent going round (T051, R3.5).</param>
/// <param name="UnconfirmedM">Its metres on Unconfirmed Access, which a Gravel Ride flags (T068).</param>
/// <param name="Obstacles">The Obstacles on it (T069), by kind, most first: listed, as SPEC R9.1 asks.</param>
public sealed record Breakdown(
    double LengthM, double OffRoadPercent, double TrafficFreePercent, double ConnectorLengthM, double RetraceLengthM,
    double FlowM = 0, double LongestGreenM = 0, double MainRoadLengthM = 0, double GreeneryPercent = 0,
    double CorridorLengthM = 0, string? MainCorridor = null, double MainCorridorM = 0, double OpenGroundM = 0, double WatersidePercent = 0,
    int Crossings = 0, int MainRoadCrossings = 0, double ClimbM = 0, double DescentM = 0, double UnconfirmedM = 0,
    IReadOnlyList<ObstacleCount>? Obstacles = null)
{
    /// <summary>Its share off-road, 0 to 1 (T104).</summary>
    public double OffRoadShare => OffRoadPercent / 100;

    /// <summary>Its climbing per km, in metres (T104); none on a route of no length.</summary>
    public double ClimbPerKm => LengthM > 0 ? ClimbM / LengthM * 1000 : 0;

    public static Breakdown Of(NetworkSlice slice, Loop loop)
    {
        ArgumentNullException.ThrowIfNull(slice);
        ArgumentNullException.ThrowIfNull(loop);

        var byGround = new double[Enum.GetValues<GroundKind>().Length];
        var covered = new HashSet<int>();
        var retrace = 0.0;
        var mainRoad = 0.0;
        var green = 0.0;
        var openGround = 0.0;
        var water = 0.0;
        var climb = 0.0;
        var descent = 0.0;
        var unconfirmed = 0.0;
        // A crossing way cut into Segments is one Crossing: counted as the Loop steps onto it.
        int crossings = 0, mainRoadCrossings = 0;
        long? onCrossing = null;
        var byCorridor = new Dictionary<string, double>(StringComparer.Ordinal);
        // Shares add up: half a gate on each side of a junction passed through is one gate.
        var obstacles = new double[Enum.GetValues<ObstacleKind>().Length];
        foreach (var traversal in loop.Traversals)
        {
            var segment = slice.Segments[traversal.Segment];
            byGround[(int)segment.Ground] += segment.LengthM;
            retrace += segment.RetracedMetres(covered);
            if (segment.MainRoad) mainRoad += segment.LengthM;
            green += segment.Greenery * segment.LengthM;
            if (segment.Key.IsOpenGround) openGround += segment.LengthM;
            if (segment.Access == AccessKind.Unconfirmed) unconfirmed += segment.LengthM;
            water += segment.Waterside * segment.LengthM;
            climb += traversal.Forward ? segment.AscentM : segment.DescentM;
            descent += traversal.Forward ? segment.DescentM : segment.AscentM;
            if (segment.Crossing is null) onCrossing = null;
            else if (onCrossing != segment.Key.OsmWayId)
            {
                onCrossing = segment.Key.OsmWayId;
                crossings++;
                if (segment.CrossesMainRoad) mainRoadCrossings++;
            }
            if (segment.OnCorridor) byCorridor[segment.Corridor] = byCorridor.GetValueOrDefault(segment.Corridor) + segment.LengthM;
            foreach (var obstacle in segment.Obstacles ?? []) obstacles[(int)obstacle.Kind] += obstacle.Share;
            segment.Cover(covered);
        }

        var length = byGround.Sum();
        // Ties go to the first name, so the Breakdown is the same every time.
        var main = byCorridor.OrderByDescending(c => c.Value).ThenBy(c => c.Key, StringComparer.Ordinal).FirstOrDefault();
        return new Breakdown(
            LengthM: length,
            OffRoadPercent: 100 * byGround[(int)GroundKind.OffRoad] / length,
            TrafficFreePercent: 100 * byGround[(int)GroundKind.TrafficFree] / length,
            ConnectorLengthM: byGround[(int)GroundKind.Connector],
            RetraceLengthM: retrace,
            FlowM: Flow.Of(slice, loop),
            LongestGreenM: Flow.Longest(slice, loop),
            MainRoadLengthM: mainRoad,
            GreeneryPercent: length > 0 ? 100 * green / length : 0,
            CorridorLengthM: byCorridor.Values.Sum(),
            MainCorridor: main.Key,
            MainCorridorM: main.Value,
            OpenGroundM: openGround,
            WatersidePercent: length > 0 ? 100 * water / length : 0,
            Crossings: crossings,
            MainRoadCrossings: mainRoadCrossings,
            ClimbM: climb,
            DescentM: descent,
            UnconfirmedM: unconfirmed,
            Obstacles: [.. obstacles.Select((share, kind) => new ObstacleCount((ObstacleKind)kind, (int)Math.Ceiling(share - 1e-9)))
                .Where(o => o.Count > 0).OrderByDescending(o => o.Count).ThenBy(o => o.Kind)]);
    }
}

/// <summary>How many Obstacles of a kind a Loop passes (T069).</summary>
public readonly record struct ObstacleCount(ObstacleKind Kind, int Count);
