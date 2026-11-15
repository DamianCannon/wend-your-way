using WendYourWay.Core.Network;

namespace WendYourWay.Core.Loops;

/// <summary>A junction a route passes: where on its line, how many ways meet there, and what it goes onto.</summary>
/// <param name="Point">Its place in the route's line.</param>
/// <param name="Ways">How many ways meet there: three or more is a choice.</param>
/// <param name="Onto">What the route goes onto from it: a way's name, or its kind ("the footpath").</param>
public sealed record RouteJunction(int Point, int Ways, string Onto);

/// <summary>How a route turns at a turn point.</summary>
public enum TurnKind
{
    Left,
    Right,
    SharpLeft,
    SharpRight,
    TurnBack,
}

/// <summary>A turn point: where, how far along, which way, and in words.</summary>
public sealed record TurnPoint(NodePoint At, double AlongM, TurnKind Kind, string Words);

/// <summary>
/// The turns worth saying on a route (PLAN T100, STORY-043; user: "45° over 30 m"): at a junction
/// with a choice, where the bearing over the 30 m after differs from the 30 m before by 45 degrees
/// or more; junctions closer than 30 m are one, the sharpest speaking. Over 30 m, not each way's
/// first metres, so a road crossed by a zig-zag, or a pavement's kink, says nothing: on the user's
/// rated Loops, about 3.5 a km.
/// </summary>
public static class TurnPoints
{
    /// <summary>How far before and after a junction its turn is measured, and how close two are one.</summary>
    public const double ReachM = 30;

    /// <summary>The least turn said, in degrees.</summary>
    public const double LeastTurn = 45;

    private const double SharpFrom = 135;
    private const double BackFrom = 165;

    public static IReadOnlyList<TurnPoint> Find(IReadOnlyList<NodePoint> line, IReadOnlyList<RouteJunction> junctions)
    {
        ArgumentNullException.ThrowIfNull(line);
        ArgumentNullException.ThrowIfNull(junctions);
        if (line.Count < 2) return [];
        var along = new double[line.Count];
        for (var i = 1; i < line.Count; i++) along[i] = along[i - 1] + line[i].DistanceTo(line[i - 1]);

        // Each junction with a choice, by how far it turns (signed: right positive).
        var found = new List<(TurnPoint Point, double Turn)>();
        foreach (var junction in junctions.Where(j => j.Ways >= 3 && j.Point > 0 && j.Point < line.Count - 1).OrderBy(j => j.Point))
        {
            var d = along[junction.Point];
            var turn = Signed(Bearing(At(line, along, d), At(line, along, d + ReachM)) - Bearing(At(line, along, d - ReachM), At(line, along, d)));
            var point = new TurnPoint(line[junction.Point], d, KindOf(turn), $"{Said(turn)} onto {junction.Onto}");
            // Closer than the reach to the one before: one turn, the sharper of the two.
            if (found.Count > 0 && d - found[^1].Point.AlongM < ReachM)
            {
                if (Math.Abs(turn) > Math.Abs(found[^1].Turn)) found[^1] = (point, turn);
            }
            else
            {
                found.Add((point, turn));
            }
        }
        return [.. found.Where(f => Math.Abs(f.Turn) >= LeastTurn).Select(f => f.Point)];
    }

    private static TurnKind KindOf(double turn) => Math.Abs(turn) switch
    {
        >= BackFrom => TurnKind.TurnBack,
        >= SharpFrom => turn > 0 ? TurnKind.SharpRight : TurnKind.SharpLeft,
        _ => turn > 0 ? TurnKind.Right : TurnKind.Left,
    };

    private static string Said(double turn) => KindOf(turn) switch
    {
        TurnKind.TurnBack => "Turn back",
        TurnKind.SharpRight => "Sharp right",
        TurnKind.SharpLeft => "Sharp left",
        TurnKind.Right => "Right",
        _ => "Left",
    };

    // The point d metres along the line, held to its ends.
    private static NodePoint At(IReadOnlyList<NodePoint> line, double[] along, double d)
    {
        d = Math.Clamp(d, 0, along[^1]);
        var i = Array.BinarySearch(along, d);
        if (i >= 0) return line[i];
        i = ~i;
        var f = (d - along[i - 1]) / (along[i] - along[i - 1]);
        return new NodePoint(line[i - 1].X + (f * (line[i].X - line[i - 1].X)), line[i - 1].Y + (f * (line[i].Y - line[i - 1].Y)));
    }

    // Degrees clockwise from north.
    private static double Bearing(NodePoint a, NodePoint b) => Math.Atan2(b.X - a.X, b.Y - a.Y) * 180 / Math.PI;

    // Into -180 to 180.
    private static double Signed(double degrees) => ((degrees % 360) + 540) % 360 - 180;
}
