using WendYourWay.Core.Scoring;
using WendYourWay.Data;
using WendYourWay.Data.Planning;

namespace WendYourWay.Web.Specs;

/// <summary>
/// The stand-in places and Loops the planner's specs share (T124c): the page's in bUnit and the
/// session's plain ones. Public landmarks, never the user's home.
/// </summary>
internal static class SampleLoops
{
    internal static readonly LatLon KenwoodHouse = new(51.5712, -0.1677);

    internal static readonly LatLon ParliamentHill = new(51.5597, -0.1607);

    //  A 9.71 km Loop: 62% Off-road, 17% Traffic-free, 1.99 km of Connectors, 0.34 km of Retrace,
    //  3.2 km in long green stretches, the longest 2.4 km, a 120 m Main Road link, and 54% of it
    //  beside green areas; 143 m of climbing and 141 m of descent, up from Kenwood House to
    //  Parliament Hill and down.
    internal static readonly PlannedLoop Planned = new(
        Line: [KenwoodHouse, ParliamentHill, KenwoodHouse],
        Start: KenwoodHouse,
        Anchors: [ParliamentHill, new LatLon(51.5650, -0.1500)],
        Breakdown: new Breakdown(LengthM: 9710, OffRoadPercent: 62, TrafficFreePercent: 17, ConnectorLengthM: 1990, RetraceLengthM: 340,
            FlowM: 3200, LongestGreenM: 2400, MainRoadLengthM: 120, GreeneryPercent: 54,
            CorridorLengthM: 4100, MainCorridor: "Capital Ring", MainCorridorM: 3200, OpenGroundM: 650, WatersidePercent: 31, Crossings: 7, MainRoadCrossings: 2, ClimbM: 143.4, DescentM: 141.2),
        Profile: [new(0, 104, KenwoodHouse), new(3000, 98, new LatLon(51.5650, -0.1500)), new(6000, 131, ParliamentHill), new(9710, 104, KenwoodHouse)]);

    //  STORY-010, R1.4, R10.5: five Loops to page through (T052), the map showing only the current one.
    internal static readonly PlannedLoop Second = Planned with
    {
        Line = [KenwoodHouse, new LatLon(51.4769, 0.0005), KenwoodHouse],
        Breakdown = Planned.Breakdown with { LengthM = 10_240 },
    };

    internal static readonly PlannedLoop Third = Planned with
    {
        Line = [KenwoodHouse, new LatLon(51.4426, -0.3006), KenwoodHouse],
        Breakdown = Planned.Breakdown with { LengthM = 9_310 },
    };
}
