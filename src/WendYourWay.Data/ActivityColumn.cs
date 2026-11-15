using WendYourWay.Core.Loops;

namespace WendYourWay.Data;

/// <summary>
/// The `activity` column of app.outings and app.saved_routes, and the <see cref="Activity"/> it holds
/// (T121: one home for what five places wrote each their own way). The column's enum has a Run and a
/// Gravel Ride only. What none means differs, so each table has its own reading, named.
/// </summary>
public static class ActivityColumn
{
    /// <summary>An Activity as the column keeps it: a Trail Ride as a Gravel Ride, as before.</summary>
    public static string Of(Activity activity) => activity == Activity.Run ? "run" : "gravel_ride";

    /// <summary>
    /// An Outing's: a run, walk or hike holds "run"; a gravel ride "gravel_ride"; a ride on the road
    /// holds none, informing no Activity of its own, and is scored and matched as a Gravel Ride (T097).
    /// </summary>
    public static Activity OfOuting(string? column) => column == "run" ? Activity.Run : Activity.GravelRide;

    /// <summary>A Saved Route's: one with none was saved before the stars, and is a Run (T115).</summary>
    public static Activity OfSavedRoute(string? column) => column == "gravel_ride" ? Activity.GravelRide : Activity.Run;
}
