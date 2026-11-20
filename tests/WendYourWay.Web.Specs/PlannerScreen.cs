using Bunit;
using WendYourWay.Data;
using WendYourWay.Data.Outings;
using WendYourWay.Data.Places;
using WendYourWay.Data.Planning;
using WendYourWay.Web.Components;
using WendYourWay.Web.Components.Pages;
using WendYourWay.Web.Planning;
using Microsoft.Extensions.DependencyInjection;

namespace WendYourWay.Web.Specs;

/// <summary>
/// The planner page in bUnit with every port it needs faked (T118): the one place that knows them,
/// so a port added or changed touches this file, not every spec file that opens the page. The
/// planner and the Outings, which specs answer differently, are given; the rest are made here and
/// named for the specs to set and read. Registered as it's made, so a spec may still register
/// another in its place before the page opens; the last registered is the one used.
/// </summary>
internal sealed class PlannerScreen : IDisposable
{
    public PlannerScreen(ILoopPlanner planner, IOutingViews? outings = null)
    {
        Map = Context.JSInterop.SetupModule("./js/map.js");
        Map.Mode = JSRuntimeMode.Loose;
        var services = Context.Services;
        services.AddSingleton(MapTiles.Of("https://tiles.example/{z}/{x}/{y}.png", "Tiles"));
        services.AddSingleton(new PlannerSettings(Home: null));
        services.AddSingleton(planner);
        services.AddSingleton(outings ?? new NoOutings());
        services.AddSingleton<ILastRequest>(LastRequest);
        services.AddSingleton<ISavedRoutes>(SavedRoutes);
        RouteMoves = new FakeRouteMoves(SavedRoutes);
        services.AddSingleton<ISavedRouteMoves>(RouteMoves);
        services.AddSingleton<IMarks>(Marks);
        services.AddSingleton<Phone.IPhoneShare>(Phone);
        services.AddSingleton<IRouteTurns>(Turns);
        services.AddSingleton<ICarParks>(CarParks);
        services.AddSingleton<ILoopPreviews>(Previews);
        services.AddSingleton<IPlaceSearch>(Places);
        services.AddSingleton<IMapUpdater>(MapUpdater);
        services.AddSingleton<IAdmin>(Admin);
        services.AddSingleton<TimeProvider>(Time);
    }

    public BunitContext Context { get; } = new();

    /// <summary>The map's JavaScript module, loose: every call answered, each one kept to look at.</summary>
    public BunitJSModuleInterop Map { get; }

    public FakeLastRequest LastRequest { get; } = new();
    public FakeSavedRoutes SavedRoutes { get; } = new();
    public FakeRouteMoves RouteMoves { get; }
    public FakeMarks Marks { get; } = new();
    public FakePhoneShare Phone { get; } = new();
    public FakeRouteTurns Turns { get; } = new();
    public FakeCarParks CarParks { get; } = new();
    public FakePreviews Previews { get; } = new();
    public FakePlaceSearch Places { get; } = new();
    public FakeMapUpdater MapUpdater { get; } = new();
    public FakeAdmin Admin { get; } = new();

    /// <summary>The time, as a spec sets it (T138).</summary>
    public SpecTime Time { get; } = new();

    /// <summary>The page, opened with <paramref name="home"/> as home (none by default).</summary>
    public IRenderedComponent<Planner> Open(LatLon? home = null)
    {
        Context.Services.AddSingleton(new PlannerSettings(home));
        return Context.Render<Planner>();
    }

    public void Dispose() => Context.Dispose();
}

/// <summary>The time, as a spec sets it.</summary>
internal sealed class SpecTime : TimeProvider
{
    public DateTimeOffset Now { get; set; } = new(2026, 11, 15, 12, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow() => Now;
}