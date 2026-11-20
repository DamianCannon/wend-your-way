using WendYourWay.Data;
using WendYourWay.Data.Outings;
using WendYourWay.Data.Planning;
using WendYourWay.Web.Components;

namespace WendYourWay.Web.Planning;

/// <summary>
/// The map as the planner draws on it (T124a): one call for each of the map's own, named as
/// <see cref="MapView"/>'s are, which implements it. Not a scene that draws what changed: the
/// planner's specs count these calls, and their order is the planner's to choose. In plain specs, a
/// fake keeps them.
/// </summary>
public interface IPlannerMap
{
    Task ShowStartAsync(LatLon start);
    Task ShowFinishAsync(LatLon finish);
    Task ClearFinishAsync();
    Task ShowLoopAsync(PlannedLoop route, bool fit = true);
    Task ShowRouteAsync(bool shown);
    Task MarkPlaceAsync(LatLon place);
    Task ClearPlaceAsync();
    Task ClearLoopAsync();
    Task ShowOutingAsync(OutingView outing);
    Task ClearOutingAsync();
    Task ShowStartsAsync(IReadOnlyList<StartOnMap> starts, int chosen);
    Task ClearStartsAsync();
    Task GoToAsync(LatLon place, int zoom);
    Task ShowMarksAsync(IReadOnlyList<MarkOnMap> marks);

    /// <summary>The route's notes, for Explain? (T157): none to take them off.</summary>
    Task ShowNotesAsync(IReadOnlyList<NoteOnMap> notes);
}
