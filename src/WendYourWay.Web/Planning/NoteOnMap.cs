using System.Globalization;
using WendYourWay.Core.Network;
using WendYourWay.Core.Scoring;
using WendYourWay.Data;
using WendYourWay.Data.Planning;

namespace WendYourWay.Web.Planning;

/// <summary>
/// A route's note as Explain? says it on the map (T157, #161): where, what kind (for its look), in the
/// Loop card's words, and from which zoom it's said. Zoomed out, the stretches; in to the streets, each
/// Crossing and Obstacle too.
/// </summary>
public sealed record NoteOnMap(LatLon At, string Kind, string Words, int FromZoom)
{
    /// <summary>From this zoom, a stretch of road and the longest green stretch are said.</summary>
    public const int StretchesFrom = 12;

    /// <summary>From this zoom, in to the streets, each Crossing and Obstacle too.</summary>
    public const int PointsFrom = 15;

    public static NoteOnMap Of(RouteNote note)
    {
        ArgumentNullException.ThrowIfNull(note);
        var n = note.Note;
        return n.Kind switch
        {
            NoteKind.Crossing => new(note.At, "crossing", Crossing(n), PointsFrom),
            NoteKind.Obstacle => new(note.At, "obstacle", Obstacle(n.Obstacle), PointsFrom),
            NoteKind.Road => new(note.At, n.MainRoadM > 0 ? "road main-road" : "road", Road(n), StretchesFrom),
            NoteKind.Customers => new(note.At, "customers", $"For customers only: {Length(n.LengthM)}, check you may pass", StretchesFrom),
            _ => new(note.At, "green", $"Longest green: {Km(n.LengthM)}", StretchesFrom),
        };
    }

    private static string Crossing(LoopNote note) => note.Crossing switch
    {
        CrossingKind.Signals => "Crossing with lights",
        CrossingKind.Marked => "Zebra crossing",
        _ => note.MainRoad ? "Across a Main Road, no crossing" : "Across the road",
    };

    // As the Loop card names them (T069), with a capital.
    private static string Obstacle(ObstacleKind? kind) => kind switch
    {
        ObstacleKind.Gate => "Gate",
        ObstacleKind.CycleBarrier => "Cycle barrier",
        ObstacleKind.KissingGate => "Kissing gate",
        ObstacleKind.Turnstile => "Turnstile",
        ObstacleKind.MotorcycleBarrier => "Motorcycle barrier",
        ObstacleKind.Stile => "Stile",
        ObstacleKind.CattleGrid => "Cattle grid",
        ObstacleKind.Ford => "Ford",
        _ => "Obstacle",
    };

    // "450 m of road", with how much of it is beside or on a Main Road.
    private static string Road(LoopNote note)
    {
        var road = $"{Length(note.LengthM)} of road";
        if (note.MainRoadM <= 0) return road;
        var where = note.Pavement ? "beside a Main Road" : "on a Main Road";
        return note.MainRoadM >= note.LengthM - 1 ? $"{road}, all {where}" : $"{road}, {Length(note.MainRoadM)} {where}";
    }

    private static string Length(double metres) => metres < 1000 ? $"{Math.Round(metres).ToString("0", CultureInfo.InvariantCulture)} m" : Km(metres);

    private static string Km(double metres) => $"{(metres / 1000).ToString("0.00", CultureInfo.InvariantCulture)} km";
}
