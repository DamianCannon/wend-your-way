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

    private MapTiles TilesFor(string? thunderforestKey, string? osKey = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Thunderforest:ApiKey"] = thunderforestKey, ["OsMaps:ApiKey"] = osKey })
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
            Is.EqualTo(new[] { "Thunderforest Outdoors", "Thunderforest OpenCycleMap", "CyclOSM", "OpenTopoMap" }));

    [Test]
    public void Without_a_Thunderforest_key_its_maps_are_left_out() =>
        Assert.That(TilesFor(null).Layers.Select(l => l.Name), Is.EqualTo(new[] { "CyclOSM", "OpenTopoMap" }));

    [Test]
    public void With_an_Ordnance_Survey_key_OS_Outdoor_is_offered_second() =>
        Assert.That(TilesFor("test-key", "os-key").Layers[1].Url,
            Is.EqualTo("https://api.os.uk/maps/raster/v1/zxy/Outdoor_3857/{z}/{x}/{y}.png?key=os-key"));

    //  A Loop is OpenStreetMap data whatever the map under it (R8.3).
    [Test]
    public void Every_map_credits_its_maker_and_OpenStreetMap() =>
        Assert.That(TilesFor("test-key", "os-key").Layers.Select(l => (l.Attribution.Contains("OpenStreetMap contributors", StringComparison.Ordinal),
                l.Attribution.Contains(l.Name.Split(' ')[0] == "OS" ? "Crown copyright" : l.Name.Split(' ')[0], StringComparison.Ordinal))),
            Has.All.EqualTo((true, true)));

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
