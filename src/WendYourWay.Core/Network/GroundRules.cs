namespace WendYourWay.Core.Network;

/// <summary>
/// The rules the Network is built by that the database holds (db/functions: classify_ground, group_same_ground,
/// build_segments), here so the rules a route is chosen by can say them (T200, #204). A spec reads the SQL and checks
/// each against it, so neither changes without the other.
/// </summary>
public static class GroundRules
{
    /// <summary>A path's sample is beside a road within this of the road's line (group_same_ground c_within_m).</summary>
    public const double BesideM = 12;

    /// <summary>Beside a dual carriageway or a road of 4+ lanes, this far (T199; c_big_within_m).</summary>
    public const double BesideBigRoadM = 25;

    /// <summary>A path is beside roads when this share of it is (c_most).</summary>
    public const double MostlyShare = 0.6;

    /// <summary>A dirt path beside roads this fast or faster, tagged, isn't Off-road (T199).</summary>
    public const int FastMph = 50;

    /// <summary>A Main Road has a real pavement at this speed or under, with pavements both sides (T181; has_pavement).</summary>
    public const int PavementMph = 30;

    /// <summary>Surfaces tagged unpaved (tagged_unpaved).</summary>
    public static IReadOnlyList<string> Unpaved { get; } =
        ["unpaved", "dirt", "earth", "ground", "grass", "gravel", "fine_gravel", "compacted", "mud", "woodchips", "sand", "pebblestone", "grass_paver"];

    /// <summary>Main Roads' highway kinds, each with its link (build_segments).</summary>
    public static IReadOnlyList<string> MainRoads { get; } = ["motorway", "trunk", "primary", "secondary"];
}
