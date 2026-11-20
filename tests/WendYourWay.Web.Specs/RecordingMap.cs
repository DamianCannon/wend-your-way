using WendYourWay.Data;
using WendYourWay.Data.Outings;
using WendYourWay.Data.Planning;
using WendYourWay.Web.Components;
using WendYourWay.Web.Planning;

namespace WendYourWay.Web.Specs;

/// <summary>The map the session draws on, in plain specs (T124c): every call kept, in order, by name and argument.</summary>
internal sealed class RecordingMap : IPlannerMap
{
    public List<(string Call, object? Argument)> Calls { get; } = [];

    /// <summary>Just the calls' names, in order.</summary>
    public IEnumerable<string> Names => Calls.Select(c => c.Call);

    /// <summary>The argument of the last call of this name.</summary>
    public object? Last(string call) => Calls.Last(c => c.Call == call).Argument;

    private Task Kept(string call, object? argument = null)
    {
        Calls.Add((call, argument));
        return Task.CompletedTask;
    }

    public Task ShowStartAsync(LatLon start) => Kept("showStart", start);
    public Task ShowFinishAsync(LatLon finish) => Kept("showFinish", finish);
    public Task ClearFinishAsync() => Kept("clearFinish");
    public Task ShowLoopAsync(PlannedLoop route, bool fit = true) => Kept("showLoop", (route, fit));
    public Task ShowRouteAsync(bool shown) => Kept("showRoute", shown);
    public Task MarkPlaceAsync(LatLon place) => Kept("markPlace", place);
    public Task ClearPlaceAsync() => Kept("clearPlace");
    public Task ClearLoopAsync() => Kept("clearLoop");
    public Task ShowOutingAsync(OutingView outing) => Kept("showOuting", outing);
    public Task ClearOutingAsync() => Kept("clearOuting");
    public Task ShowStartsAsync(IReadOnlyList<StartOnMap> starts, int chosen) => Kept("showStarts", (starts, chosen));
    public Task ClearStartsAsync() => Kept("clearStarts");
    public Task GoToAsync(LatLon place, int zoom) => Kept("goTo", (place, zoom));
    public Task ShowMarksAsync(IReadOnlyList<MarkOnMap> marks) => Kept("showMarks", marks);
    public Task ShowNotesAsync(IReadOnlyList<NoteOnMap> notes) => Kept("showNotes", notes);
}
