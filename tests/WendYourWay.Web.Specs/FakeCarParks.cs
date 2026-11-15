using WendYourWay.Data.Planning;

namespace WendYourWay.Web.Specs;

/// <summary>A stand-in for the car parks around here (T060).</summary>
internal sealed class FakeCarParks : ICarParks
{
    /// <summary>What it was asked: which view, for what Distance and Activity.</summary>
    public List<(MapBounds View, double DistanceKm, WendYourWay.Core.Loops.Activity Activity)> Asked { get; } = [];

    /// <summary>What it finds, whatever the view.</summary>
    public CarParksAround Answer { get; set; } = new([], []);

    public Task<CarParksAround> AroundAsync(MapBounds view, double distanceKm, WendYourWay.Core.Loops.Activity activity = WendYourWay.Core.Loops.Activity.Run, CancellationToken cancellationToken = default)
    {
        Asked.Add((view, distanceKm, activity));
        return Task.FromResult(Answer);
    }
}
