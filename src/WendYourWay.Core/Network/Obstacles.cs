namespace WendYourWay.Core.Network;

/// <summary>
/// Something on a way that slows an Activity without making it unlawful (CONTEXT.md: Obstacle;
/// PLAN T069), from OpenStreetMap's barrier nodes and fords. Swing, wicket, sliding and Hampshire
/// gates are gates.
/// </summary>
public enum ObstacleKind
{
    Gate,
    CycleBarrier,
    KissingGate,
    Turnstile,
    MotorcycleBarrier,
    Stile,
    CattleGrid,
    Ford,
}

/// <summary>
/// An Obstacle on a Segment, and how much of it is the Segment's: all of one inside it, half of
/// one at a junction where two Segments meet, so a Loop passing through pays for it once.
/// </summary>
public readonly record struct ObstacleShare(ObstacleKind Kind, double Share);
