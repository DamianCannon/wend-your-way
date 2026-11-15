using WendYourWay.Core.Loops;

namespace WendYourWay.Data.Planning;

/// <summary>
/// The app's score for a route (T097), from which its stars are drawn: the Activity it was planned
/// for, its score, and the share of it on the map.
/// </summary>
/// <param name="OffRoadShare">Its share off-road, 0 to 1 (T104); none if not known.</param>
/// <param name="ClimbPerKm">Its climbing per km, in metres (T104); none if not known.</param>
public sealed record RouteScore(Activity Activity, double Score, double MatchedShare, double? OffRoadShare = null, double? ClimbPerKm = null);
