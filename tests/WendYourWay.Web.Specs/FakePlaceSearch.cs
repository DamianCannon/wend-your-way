using WendYourWay.Data;
using WendYourWay.Data.Places;

namespace WendYourWay.Web.Specs;

/// <summary>A stand-in for searching places by name and postcode (T064).</summary>
internal sealed class FakePlaceSearch : IPlaceSearch
{
    /// <summary>What it was asked, in order: what was typed, and the middle of the map.</summary>
    public List<(string Text, LatLon Middle)> Asked { get; } = [];

    /// <summary>What every search finds.</summary>
    public IReadOnlyList<FoundPlace> Answer { get; set; } = [];

    /// <summary>
    /// When set, the first search waits for it, heedless of being cancelled, as a query already in
    /// PostgreSQL finishes.
    /// </summary>
    public TaskCompletionSource? HoldFirst { get; set; }

    /// <summary>The postcode nearest wherever it's asked; none if null.</summary>
    public FoundPlace? Nearest { get; set; }

    /// <summary>Where it was asked for the nearest postcode.</summary>
    public List<LatLon> AskedNearest { get; } = [];

    public Task<FoundPlace?> NearestPostcodeAsync(LatLon at, CancellationToken cancellationToken = default)
    {
        lock (AskedNearest) AskedNearest.Add(at);
        return Task.FromResult(Nearest);
    }

    public async Task<IReadOnlyList<FoundPlace>> SearchAsync(string text, LatLon middle, CancellationToken cancellationToken = default)
    {
        int asked;
        lock (Asked)
        {
            Asked.Add((text, middle));
            asked = Asked.Count;
        }
        if (asked == 1 && HoldFirst is { } hold) await hold.Task;
        return Answer;
    }
}
