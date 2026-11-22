using Bunit;
using WendYourWay.Data;
using WendYourWay.Web.Components;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

namespace WendYourWay.Web.Specs;

/// <summary>
/// The map under everything: a choice of maps, since different ones suit different Activities
/// (user, 2026-10-19), Thunderforest Outdoors first when there's a key, each credited.
/// </summary>
public class ShowingTheMap
{
    private BunitContext _context = null!;
    private BunitJSModuleInterop _map = null!;

    [SetUp]
    public void Arrange()
    {
        _context = new BunitContext();
        _map = _context.JSInterop.SetupModule("./js/map.js");
        _map.SetupVoid("create", _ => true);
    }

    [TearDown]
    public void Dispose() => _context.Dispose();

    private MapTiles TilesFor(string? thunderforestKey, string? osKey = null, string? osPremium = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Thunderforest:ApiKey"] = thunderforestKey, ["OsMaps:ApiKey"] = osKey, ["OsMaps:Premium"] = osPremium })
            .Build();
        _context.Services.AddSingleton(MapTiles.From(configuration));
        _context.Render<MapView>();
        return (MapTiles)_map.VerifyInvoke("create").Arguments[1]!;
    }

    [Test]
    public void The_first_map_is_Thunderforest_Outdoors_with_the_configured_key()
    {
        var first = TilesFor("test-key").Layers[0];
        Assert.That((first.Name, first.Url), Is.EqualTo(("Thunderforest Outdoors", "https://tile.thunderforest.com/outdoors/{z}/{x}/{y}{r}.png?apikey=test-key")));
    }

    [Test]
    public void The_other_maps_can_be_chosen_on_the_map() =>
        Assert.That(TilesFor("test-key").Layers.Select(l => l.Name),
            Is.EqualTo(new[] { "Thunderforest Outdoors", "Thunderforest OpenCycleMap", "CyclOSM", "OpenTopoMap", "Aerial photos" }));

    [Test]
    public void Without_a_Thunderforest_key_its_maps_are_left_out() =>
        Assert.That(TilesFor(null).Layers.Select(l => l.Name), Is.EqualTo(new[] { "CyclOSM", "OpenTopoMap", "Aerial photos" }));

    [Test]
    public void With_an_Ordnance_Survey_key_OS_Outdoor_is_offered_second() =>
        Assert.That(TilesFor("test-key", "os-key").Layers[1].Url,
            Is.EqualTo("https://api.os.uk/maps/raster/v1/zxy/Outdoor_3857/{z}/{x}/{y}.png?key=os-key"));

    private int OsOutdoorsDeepestZoom(string? osPremium) =>
        TilesFor("test-key", "os-key", osPremium).Layers.Single(l => l.Name == "OS Outdoor").MaxNativeZoom;

    //  On OS's Premium plan its closer zooms (17–20) are within the free allowance (T167, #171): drawn
    //  by OS, not enlarged from zoom 16.
    [Test]
    public void On_OS_s_Premium_plan_OS_Outdoor_is_drawn_by_OS_at_every_zoom() =>
        Assert.That(OsOutdoorsDeepestZoom("true"), Is.EqualTo(20));

    //  The free plan serves no tiles past zoom 16: enlarged from there, never blank.
    [Test]
    public void On_OS_s_free_plan_OS_Outdoor_is_enlarged_past_zoom_16() =>
        Assert.That(OsOutdoorsDeepestZoom(null), Is.EqualTo(16));

    //  Aerial photos show whether a path is a real track, gravel or overgrown (T166, #170): Esri's
    //  World Imagery, no key.
    [Test]
    public void Aerial_photos_are_Esri_s_World_Imagery() =>
        Assert.That(TilesFor(null).Layers.Single(l => l.Name == "Aerial photos").Url,
            Is.EqualTo("https://server.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/{z}/{y}/{x}"));

    private static readonly Dictionary<string, string> Makers = new()
    {
        ["Thunderforest Outdoors"] = "Thunderforest", ["Thunderforest OpenCycleMap"] = "Thunderforest", ["OS Outdoor"] = "Crown copyright",
        ["CyclOSM"] = "CyclOSM", ["OpenTopoMap"] = "OpenTopoMap", ["Aerial photos"] = "Esri", ["Signed trails"] = "Waymarked Trails",
    };

    //  A Loop is OpenStreetMap data whatever the map under it (R8.3).
    [Test]
    public void Every_map_credits_its_maker_and_OpenStreetMap() =>
        Assert.That(TilesFor("test-key", "os-key").Layers.Select(l => (l.Name, l.Attribution.Contains("OpenStreetMap contributors", StringComparison.Ordinal),
                l.Attribution.Contains(Makers[l.Name], StringComparison.Ordinal))),
            Has.All.Matches<(string, bool, bool)>(credit => credit.Item2 && credit.Item3));

    //  Signed walking, cycling and MTB routes over any map (T166): Waymarked Trails', no key.
    [Test]
    public void Signed_trails_can_be_drawn_over_any_map() =>
        Assert.That(TilesFor(null).Overlays.Select(o => (o.Name, string.Join(" ", o.Urls))),
            Is.EqualTo(new[] { ("Signed trails", "https://tile.waymarkedtrails.org/hiking/{z}/{x}/{y}.png https://tile.waymarkedtrails.org/cycling/{z}/{x}/{y}.png https://tile.waymarkedtrails.org/mtb/{z}/{x}/{y}.png") }));

    [Test]
    public void Every_overlay_credits_its_maker_and_OpenStreetMap() =>
        Assert.That(TilesFor(null).Overlays.Select(o => o.Attribution.Contains("OpenStreetMap contributors", StringComparison.Ordinal) && o.Attribution.Contains(Makers[o.Name], StringComparison.Ordinal)),
            Has.All.True);

    //  If the map can't be made (Leaflet didn't load, say), later calls fail rather than wait
    //  forever, so the page's own error handling can take over.
    [Test]
    public async Task When_the_map_can_t_be_made_later_calls_fail_instead_of_waiting()
    {
        var context = new BunitContext();
        try
        {
            var map = context.JSInterop.SetupModule("./js/map.js");
            map.SetupVoid("create", _ => true).SetException(new JSException("L is not defined"));
            context.Services.AddSingleton(MapTiles.Of("https://tiles.example/{z}/{x}/{y}.png", "Tiles"));
            var view = context.Render<MapView>();
            var showing = view.InvokeAsync(() => view.Instance.ShowStartAsync(new LatLon(51.5712, -0.1677)));
            Assert.That(await Task.WhenAny(showing, Task.Delay(TimeSpan.FromSeconds(5))), Is.SameAs(showing), "the call is still waiting");
            Assert.That(showing.Exception?.InnerException, Is.InstanceOf<JSException>());
        }
        finally
        {
            context.Dispose();
        }
    }
}
