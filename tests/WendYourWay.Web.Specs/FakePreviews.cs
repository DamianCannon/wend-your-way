using WendYourWay.Core.Loops;
using WendYourWay.Data;
using WendYourWay.Data.Planning;

namespace WendYourWay.Web.Specs;

/// <summary>A stand-in for working out Loop 1 from a place, to compare places (T062).</summary>
internal sealed class FakePreviews : ILoopPreviews
{
    /// <summary>What it was asked, in order: from where, for what Distance, at what Leans.</summary>
    public List<(LatLon Start, double DistanceKm, Leans Leans)> Asked { get; } = [];

    /// <summary>What each place's Loop 1 is; a place not here has none.</summary>
    public Dictionary<LatLon, LoopPreview> Answers { get; } = [];

    /// <summary>Set to hold every answer back until it's completed.</summary>
    public TaskCompletionSource? Gate { get; set; }

    /// <summary>The Activity of each, in order.</summary>
    public List<Activity> Activities { get; } = [];

    /// <summary>The tokens it was asked with, to see them cancelled.</summary>
    public List<CancellationToken> Tokens { get; } = [];

    public async Task<LoopPreview?> PreviewAsync(LatLon start, double distanceKm, Leans leans, Activity activity = Activity.Run, CancellationToken cancellationToken = default)
    {
        lock (Asked)
        {
            Asked.Add((start, distanceKm, leans));
            Activities.Add(activity);
            Tokens.Add(cancellationToken);
        }
        if (Gate is { } gate) await gate.Task.WaitAsync(cancellationToken);
        return Answers.GetValueOrDefault(start);
    }
}
