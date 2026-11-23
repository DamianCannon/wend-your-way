using System.Text.RegularExpressions;
using Bunit;
using WendYourWay.Web.Components;
using Microsoft.Extensions.DependencyInjection;

namespace WendYourWay.Web.Specs;

/// <summary>
/// A key to the map (T177, #181; user: "I also wonder if we might want to add a key to the map now that we
/// have quite a few overlays?"): in a corner, only what's on screen, told by the map as it changes;
/// collapsible, and that remembered.
/// </summary>
public class ReadingTheMapKey
{
    private BunitContext _context = null!;
    private BunitJSModuleInterop _map = null!;
    private IRenderedComponent<MapView> _view = null!;

    [SetUp]
    public void Arrange()
    {
        _context = new BunitContext();
        _map = _context.JSInterop.SetupModule("./js/map.js");
        _map.SetupVoid("create", _ => true).SetVoidResult();
        _map.SetupVoid("rememberKeyCollapsed", _ => true).SetVoidResult();
        _context.Services.AddSingleton(MapTiles.Of("https://tiles/{z}/{x}/{y}.png", "Map"));
        _view = _context.Render<MapView>();
    }

    [TearDown]
    public void Dispose()
    {
        _view.Dispose();
        _context.Dispose();
    }

    // The map says what's on screen, and whether the key was last left collapsed.
    private void OnScreen(bool collapsed, params string[] shown) =>
        _view.InvokeAsync(() => _view.Instance.KeyShown(shown, [], collapsed));

    // On, but not drawn at this zoom (T180): cafés from 13, say.
    private void OnButNotDrawn(string[] shown, string[] waiting) =>
        _view.InvokeAsync(() => _view.Instance.KeyShown(shown, waiting, false));

    private string[] Entries() => _view.FindAll(".map-key li").Select(li => li.TextContent.Trim()).ToArray();

    [Test]
    public void With_nothing_on_screen_there_is_no_key()
    {
        OnScreen(false);
        Assert.That(_view.FindAll(".map-key"), Is.Empty);
    }

    [Test]
    public void A_Loop_on_screen_is_keyed() =>
        Assert.That(KeyFor("loop"), Is.EqualTo(new[] { "The Loop" }));

    [Test]
    public void Only_what_s_on_screen_is_keyed()
    {
        OnScreen(false, "Car parks");
        Assert.That(Entries(), Is.EqualTo(new[] { "Car parks" }));
    }

    [Test]
    public void What_s_turned_off_leaves_the_key()
    {
        OnScreen(false, "loop", "Car parks");
        OnScreen(false, "loop");
        Assert.That(Entries(), Is.EqualTo(new[] { "The Loop" }));
    }

    private string[] KeyFor(string shown)
    {
        OnScreen(false, shown);
        return Entries();
    }

    //  The Loop's other marks, each only when it has them.
    [Test]
    public void A_Loop_s_stops_avoids_and_gaps_are_keyed() =>
        Assert.That(new[] { "stops", "avoids", "map-gaps", "possible-gaps", "not-on-map" }.SelectMany(KeyFor), Is.EqualTo(new[]
        {
            "A stop on the way", "Avoided", "A gap in the loop", "Possible link nearby: click to include it",
            "Not on the map",
        }));

    //  T180 (#184): the start and finish pins, the km markers and chevrons, and Explain's notes too.
    [Test]
    public void The_start_finish_km_markers_and_notes_are_keyed() =>
        Assert.That(new[] { "start", "finish", "km", "notes" }.SelectMany(KeyFor), Is.EqualTo(new[]
        {
            "Start", "Finish", "Every km, and which way round", "Why it goes this way (Explain?)",
        }));

    //  An overlay switched on but not drawn at this zoom says so, rather than keying what isn't there (T180).
    [Test]
    public void An_overlay_not_drawn_at_this_zoom_says_zoom_in()
    {
        OnButNotDrawn(["OS paths"], ["OS paths"]);
        Assert.That(Entries(), Is.EqualTo(new[] { "OS paths · zoom in to see" }));
    }

    [Test]
    public void A_group_not_drawn_at_this_zoom_says_it_in_its_heading()
    {
        OnButNotDrawn(["Cafés and toilets"], ["Cafés and toilets"]);
        Assert.That(_view.Find(".map-key h3").TextContent, Is.EqualTo("Cafés and toilets · zoom in to see"));
    }

    [Test]
    public void An_overlay_drawn_says_nothing_more()
    {
        OnButNotDrawn(["OS paths", "Car parks"], ["Car parks"]);
        Assert.That(Entries(), Is.EqualTo(new[] { "OS paths", "Car parks · zoom in to see" }));
    }

    //  T182 (#186): where the Loop runs on ways for customers only, striped.
    [Test]
    public void Ways_for_customers_only_are_keyed() =>
        Assert.That(KeyFor("customers"), Is.EqualTo(new[] { "For customers only: check you may pass" }));

    [Test]
    public void Blocked_stretches_are_keyed() =>
        Assert.That(KeyFor("blocked"), Is.EqualTo(new[] { "Never used (blocked)" }));

    //  An Outing's Segments are coloured by ground.
    [Test]
    public void An_Outing_is_keyed_by_its_ground() =>
        Assert.That(KeyFor("outing"), Is.EqualTo(new[] { "Off-road", "Traffic-free", "Connector", "Your track", "Gap" }));

    //  Off-road networks' five shades, by the network's size (T022b).
    [Test]
    public void Off_road_networks_are_keyed_by_size() =>
        Assert.That(KeyFor("Off-road networks"), Is.EqualTo(new[] { "Under 10 km", "10–30 km", "30–100 km", "100–300 km", "Over 300 km" }));

    //  Signed trails' colours say the route's level, as Waymarked Trails draws them.
    [Test]
    public void Signed_trails_are_keyed_by_level() =>
        Assert.That(KeyFor("Signed trails"), Is.EqualTo(new[] { "International", "National", "Regional", "Local" }));

    [Test]
    public void The_other_overlays_are_keyed_by_name() =>
        Assert.That(new[] { "Open access land", "OS paths", "Gravel hotspots", "Cafés and toilets" }.SelectMany(KeyFor),
            Is.EqualTo(new[] { "Open access land", "OS paths", "Gravel hotspots", "Café", "Toilets" }));

    //  Sections keep the key's own order (the Loop first), whatever order the map gives them in.
    [Test]
    public void The_Loop_comes_first()
    {
        OnScreen(false, "Car parks", "loop");
        Assert.That(Entries(), Is.EqualTo(new[] { "The Loop", "Car parks" }));
    }

    //  Single entries first, then the headed groups below them, for a visual break (user: "move [them] up to the top
    //  as they are not containers so the containers should sit below"); Cafés and toilets a group like the others.
    [Test]
    public void Single_entries_come_before_the_groups()
    {
        OnScreen(false, "Cafés and toilets", "Signed trails", "Off-road networks", "Car parks", "Gravel hotspots", "OS paths", "Open access land");
        Assert.That(_view.FindAll(".map-key h3, .map-key li").Select(e => e.TextContent.Trim()), Is.EqualTo(new[]
        {
            "Open access land", "OS paths", "Gravel hotspots", "Car parks",
            "Off-road networks", "Under 10 km", "10–30 km", "30–100 km", "100–300 km", "Over 300 km",
            "Signed trails", "International", "National", "Regional", "Local",
            "Cafés and toilets", "Café", "Toilets",
        }));
    }

    //  Overlays with more than one entry are headed by their name.
    [Test]
    public void An_overlay_with_several_entries_is_headed_by_its_name()
    {
        OnScreen(false, "Signed trails");
        Assert.That(_view.Find(".map-key h3").TextContent, Is.EqualTo("Signed trails"));
    }

    //  One entry is its own heading.
    [Test]
    public void An_overlay_with_one_entry_has_no_heading()
    {
        OnScreen(false, "OS paths");
        Assert.That(_view.FindAll(".map-key h3"), Is.Empty);
    }

    [Test]
    public void Collapsing_the_key_hides_its_entries()
    {
        OnScreen(false, "loop");
        _view.Find(".map-key button").Click();
        Assert.That(Entries(), Is.Empty);
    }

    [Test]
    public void Collapsing_the_key_is_remembered()
    {
        OnScreen(false, "loop");
        _view.Find(".map-key button").Click();
        _view.WaitForAssertion(() => Assert.That(_map.Invocations["rememberKeyCollapsed"].Single().Arguments.Single(), Is.True));
    }

    [Test]
    public void A_key_left_collapsed_opens_collapsed()
    {
        OnScreen(true, "loop");
        Assert.That((Entries(), _view.Find(".map-key button").GetAttribute("aria-expanded")), Is.EqualTo((Array.Empty<string>(), "false")));
    }

    [Test]
    public void A_collapsed_key_opens_again()
    {
        OnScreen(true, "loop");
        _view.Find(".map-key button").Click();
        Assert.That(Entries(), Is.EqualTo(new[] { "The Loop" }));
    }

    //  The key can't drift from the map: every swatch is drawn in app.css from the colour tokens the map's
    //  script reads too.
    [Test]
    public void Every_swatch_is_drawn_from_the_map_s_colour_tokens()
    {
        var css = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "WendYourWay.Web", "wwwroot", "css", "app.css"));
        var unstyled = MapKey.Sections.SelectMany(s => s.Entries).Select(e => e.Swatch).Distinct()
            .Where(swatch => !Regex.IsMatch(css, $@"\.swatch\.{Regex.Escape(swatch)}\s*\{{[^}}]*var\(--", RegexOptions.None, TimeSpan.FromSeconds(1)));
        Assert.That(unstyled, Is.Empty);
    }

    private static string RepositoryRoot()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "README.md"))) folder = folder.Parent;
        return folder?.FullName ?? throw new DirectoryNotFoundException("No README.md above the specs.");
    }
}
