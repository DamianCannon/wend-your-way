using WendYourWay.Core.Loops;
using WendYourWay.Data;
using WendYourWay.Data.Planning;
using WendYourWay.Web.Components;

namespace WendYourWay.Web.Planning;

/// <summary>
/// Car parks around here (T060; out of the planner's session, T124d): those in view of the map, read
/// as the user leaves it, from <see cref="CarParkZoom"/>, the best for the Distance listed and every
/// one marked; and Loop 1 from each of the first listed, compared before the user commits (T062).
/// "We can just select from the car parks shown on the map and decide for ourself how far away they
/// might be" (user, 2026-11-02). Told what it needs of the Request by the session, which owns it.
/// </summary>
internal sealed class StartsAround(ICarParks carParks, ILoopPreviews previews, ILogger logger) : IDisposable
{
    /// <summary>The least zoom car parks are read at.</summary>
    internal const int CarParkZoom = 11;

    // Compare before you commit (T062): Loop 1 from each of the first places listed, one at a time,
    // kept for the app's run by place, Distance and Leans; stopped when the list changes or a
    // Request starts, and taken up again after.
    private const int ComparedPlaces = 5;
    private static readonly TimeSpan CompareAfter = TimeSpan.FromSeconds(0.5);
    private readonly Dictionary<(LatLon At, double DistanceKm, Leans Leans, Activity Activity), LoopPreview?> _compared = [];
    private (LatLon At, double DistanceKm, Leans Leans, Activity Activity)? _comparing;
    private CancellationTokenSource? _comparison;
    private CarParksAround? _around;

    /// <summary>Raised where the car parks or their comparing have changed, for the page to show.</summary>
    internal event Action? Changed;

    /// <summary>The part of the map in view, once the map has said.</summary>
    internal MapBounds? View { get; private set; }

    internal int Zoom { get; private set; }

    /// <summary>The car parks listed, best first; none before the map has said where it is.</summary>
    internal IReadOnlyList<FoundStart>? Listed => View is null ? null : _around?.Best ?? [];

    /// <summary>Every car park marked on the map.</summary>
    internal IReadOnlyList<FoundStart> OnMap => _around?.OnMap ?? [];

    /// <summary>The user moved or zoomed the map.</summary>
    internal void Viewed(MapViewport viewport) => (View, Zoom) = (viewport.Bounds, viewport.Zoom);

    /// <summary>Reads the car parks in view and the best of them for the Distance; none zoomed out.</summary>
    internal async Task ReadAsync(double distanceKm, Activity activity)
    {
        if (View is not { } view) return;
        try
        {
            _around = Zoom >= CarParkZoom && distanceKm > 0 ? await carParks.AroundAsync(view, distanceKm, activity) : null;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            Log.PlanningFailed(logger, e);
            _around = null;
        }
    }

    /// <summary>The places listed, or every car park in view, marked on the map, the start's marked as chosen.</summary>
    internal Task MarkAsync(IPlannerMap map, FoundStart? chosen)
    {
        var onMap = OnMap;
        if (onMap.Count == 0) return map.ClearStartsAsync();
        return map.ShowStartsAsync([.. onMap.Select(s => new StartOnMap(s.At, Label(s)))],
            chosen is { } start ? onMap.ToList().IndexOf(start) : -1);
    }

    // A marker's label: its name, its paths and what's known of it (T066).
    private static string Label(FoundStart s) =>
        FormattableString.Invariant($"{s.Name} · {s.NetworkM / 1000:0} km of paths") + (s.Details?.Says is { Length: > 0 } details ? " · " + details : "");

    /// <summary>Compares the first places listed again, from the start; none while a Request is found.</summary>
    internal void Compare(bool finding, double distanceKm, Leans leans, Activity activity)
    {
        _comparison?.Cancel();
        _comparison?.Dispose();
        (_comparison, _comparing) = (null, null);
        if (finding || Listed is not { Count: > 0 } listed) return;
        var run = _comparison = new CancellationTokenSource();
        _ = CompareAsync([.. listed.Take(ComparedPlaces).Select(p => p.At)], distanceKm, leans, activity, run.Token);
    }

    private async Task CompareAsync(IReadOnlyList<LatLon> places, double distanceKm, Leans leans, Activity activity, CancellationToken run)
    {
        try
        {
            await Task.Delay(CompareAfter, run);
            foreach (var at in places)
            {
                if (_compared.ContainsKey((at, distanceKm, leans, activity))) continue;
                _comparing = (at, distanceKm, leans, activity);
                Changed?.Invoke();
                var preview = await previews.PreviewAsync(at, distanceKm, leans, activity, run);
                run.ThrowIfCancellationRequested();
                _compared[(at, distanceKm, leans, activity)] = preview;
                _comparing = null;
                Changed?.Invoke();
            }
        }
        catch (OperationCanceledException) when (run.IsCancellationRequested)
        {
            // The list changed, or a Request started.
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            Log.PlanningFailed(logger, e);
            _comparing = null;
        }
    }

    /// <summary>What a place listed says of its Loop 1: compared, being compared, or nothing yet.</summary>
    internal string? PreviewOf(FoundStart found, double distanceKm, Leans leans, Activity activity)
    {
        var key = (found.At, distanceKm, leans, activity);
        if (_compared.TryGetValue(key, out var preview))
            return preview is null ? "No Loop" : FormattableString.Invariant($"{preview.LengthM / 1000:0.0} km · {preview.GreenPercent:0}% green · {preview.ClimbM:0} m up");
        return _comparing == key ? "Finding a Loop…" : null;
    }

    public void Dispose()
    {
        _comparison?.Cancel();
        _comparison?.Dispose();
    }
}
