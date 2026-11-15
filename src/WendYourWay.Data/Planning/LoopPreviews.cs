using WendYourWay.Core.Loops;

namespace WendYourWay.Data.Planning;

/// <summary>What Loop 1 from a place would be (PLAN T062): its length, green share and climbing.</summary>
public sealed record LoopPreview(double LengthM, double GreenPercent, double ClimbM);

/// <summary>Works out Loop 1 from a place, to compare places before committing to one (PLAN T062).</summary>
public interface ILoopPreviews
{
    /// <summary>Loop 1 of about <paramref name="distanceKm"/> from <paramref name="start"/>, leaning as <paramref name="leans"/> say; none if there's none.</summary>
    Task<LoopPreview?> PreviewAsync(LatLon start, double distanceKm, Leans leans, Activity activity = Activity.Run, CancellationToken cancellationToken = default);
}

/// <summary>Previews made by planning a Request and keeping only Loop 1's Breakdown: the Loops themselves go.</summary>
public sealed class LoopPreviews(ILoopPlanner planner) : ILoopPreviews
{
    public async Task<LoopPreview?> PreviewAsync(LatLon start, double distanceKm, Leans leans, Activity activity = Activity.Run, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(planner);
        var offered = await planner.PlanAsync(start, distanceKm, leans, activity, progress: null, cancellationToken);
        if (offered.Count == 0) return null;
        var breakdown = offered[0].Breakdown;
        return new LoopPreview(breakdown.LengthM, breakdown.OffRoadPercent + breakdown.TrafficFreePercent, breakdown.ClimbM);
    }
}
