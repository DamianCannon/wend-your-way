using Bunit;
using WendYourWay.Core.Loops;
using WendYourWay.Core.Places;
using WendYourWay.Core.Scoring;
using WendYourWay.Data;
using WendYourWay.Web.Components;
using WendYourWay.Web.Components.Pages;
using WendYourWay.Data.Planning;
using WendYourWay.Data.Places;
using WendYourWay.Web.Planning;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.DependencyInjection;

namespace WendYourWay.Web.Specs;

/// <summary>
/// The planner page's specs, shared (T119): the page opened through <see cref="PlannerScreen"/>, its
/// fakes named, and the helpers and stand-in Loops more than one feature's specs use. STORY-029 on
/// the planner screen: set a start, choose a Distance, press Go, see the Loop and its Breakdown. The
/// planner is a stand-in; "home" is a public landmark, never the user's.
/// </summary>
public abstract class PlannerPageSpecs
{
    private protected static readonly LatLon KenwoodHouse = SampleLoops.KenwoodHouse;

    private protected PlannerScreen _screen = null!;
    private protected BunitContext _context = null!;
    private protected BunitJSModuleInterop _map = null!;
    private protected FakePlanner _planner = null!;
    private protected FakeLastRequest _last = null!;
    private protected FakeSavedRoutes _saved = null!;
    private protected FakePhoneShare _phone = null!;
    private protected FakeRouteTurns _turns = null!;
    private protected FakeMarks _marks = null!;
    private protected FakeCarParks _carParks = null!;
    private protected FakePreviews _previews = null!;
    private protected FakePlaceSearch _places = null!;
    private protected FakeNetworkDate _networkDate = null!;

    [SetUp]
    public void Arrange()
    {
        //  The page and its fakes, wired in one place (T118); the fields name them for the specs.
        _screen = new PlannerScreen(_planner = new FakePlanner());
        (_context, _map) = (_screen.Context, _screen.Map);
        (_last, _saved, _marks, _phone, _turns) = (_screen.LastRequest, _screen.SavedRoutes, _screen.Marks, _screen.Phone, _screen.Turns);
        (_carParks, _previews, _places, _networkDate) = (_screen.CarParks, _screen.Previews, _screen.Places, _screen.NetworkDate);
    }

    [TearDown]
    public void Dispose() => _screen.Dispose();

    //  With a home, the screen shows it on the map as it opens, from a continuation the renderer
    //  runs later. A synchronous Click() while that's running is queued and returns at once, so a
    //  spec asserting straight after it saw "No Loops yet" (review after T030): wait for the map
    //  first, as the user does.
    private protected IRenderedComponent<Planner> Open(LatLon? home)
    {
        var screen = _screen.Open(home);
        if (home is not null) screen.WaitForAssertion(() => Assert.That(_map.Invocations["showStart"], Is.Not.Empty));
        return screen;
    }

    private protected static readonly LatLon ParliamentHill = SampleLoops.ParliamentHill;

    //  What the Start box shows (T093): where the start is.
    private protected static string? StartText(IRenderedComponent<Planner> screen) => screen.Find(".start-search input").GetAttribute("value");

    private protected static void ClickTheMap(IRenderedComponent<Planner> screen, LatLon at) =>
        screen.InvokeAsync(() => screen.FindComponent<MapView>().Instance.MapClicked(at.Latitude, at.Longitude)).GetAwaiter().GetResult();

    private protected static readonly PlannedLoop Planned = SampleLoops.Planned;

    private protected static void SetDistance(IRenderedComponent<Planner> screen, double km) => screen.Find("input.distance").Change(km);

    private protected static void PressGo(IRenderedComponent<Planner> screen) => screen.Find("button.go").Click();

    private protected static readonly PlannedLoop Second = SampleLoops.Second;

    private protected static readonly PlannedLoop Third = SampleLoops.Third;

    private protected IRenderedComponent<Planner> OfferThree()
    {
        _planner.Answers = [Planned, Second, Third];
        var screen = Open(KenwoodHouse);
        PressGo(screen);
        screen.WaitForAssertion(() => Assert.That(_map.Invocations["showLoop"], Is.Not.Empty));
        return screen;
    }

    private protected static string Pager(IRenderedComponent<Planner> screen) => screen.Find(".pager .summary").TextContent.Trim();

    //  T076: Nudges. The route dragged from one place to another asks for a Via; clicked, Avoid
    //  this asks for an Avoid; the Loop nudged shown in place of the Loop, which Undo puts back.
    private protected static readonly LatLon Grabbed = new(51.5600, -0.1600);
    private protected static readonly LatLon Dropped = new(51.5650, -0.1500);

    private protected static void Drag(IRenderedComponent<Planner> screen) =>
        screen.InvokeAsync(() => screen.FindComponent<MapView>().Instance.RouteDragged(Grabbed.Latitude, Grabbed.Longitude, Dropped.Latitude, Dropped.Longitude)).GetAwaiter().GetResult();

    //  T083: Import a GPX as a route. "Import GPX" under Saved Routes reads the file here and
    //  has it matched onto the Network for the Activity chosen; shown alone, like an offered Loop,
    //  its name offered for saving; its gaps told on the card; refused, said in the panel, the
    //  Loops shown before kept.
    private protected const string HeathGpx = """
        <gpx xmlns="http://www.topografix.com/GPX/1/1"><trk><name>Heath from a friend</name><trkseg>
          <trkpt lat="51.5597" lon="-0.1607"/><trkpt lat="51.5605" lon="-0.1615"/><trkpt lat="51.5612" lon="-0.1624"/>
        </trkseg></trk></gpx>
        """;

    private protected static void Import(IRenderedComponent<Planner> screen, string content, string file = "heath.gpx")
    {
        OpenSavedRoutes(screen);
        screen.WaitForElement(".import-gpx");
        screen.FindComponent<InputFile>().UploadFiles(InputFileContent.CreateFromText(content, file));
    }

    //  T080: Saved Routes. The bookmark on the Loop card asks for a name, offering where it
    //  starts and its km; saved, it's listed under Saved Routes, opened on the map from there,
    //  renamed, and deleted after asking.
    private protected static void SaveAs(IRenderedComponent<Planner> screen, string name)
    {
        screen.Find(".pager .save").Click();
        screen.Find(".pager .save-name").Input(name);
        screen.Find(".pager form.saving").Submit();
    }

    private protected static void OpenSavedRoutes(IRenderedComponent<Planner> screen) =>
        screen.Find(".rail button[aria-label='Saved Routes']").Click();

    //  T078: one Undo history, of Nudges and of Requests. A stray click on the map plans from a
    //  new start; Undo brings back the Loops before, with their start, and Redo goes forward.
    // Undo or Redo there but greyed out: nothing further that way (user, 2026-11-07).
    private protected static bool Greyed(IRenderedComponent<Planner> screen, string button) => screen.Find($".pager .{button}").HasAttribute("disabled");

    //  R10.4, ADR 0007: moving the Hills slider once Loops are offered re-ranks the Loops found,
    //  while dragging, with no new search (T053).
    private protected static void Slide(IRenderedComponent<Planner> screen, double lean) => screen.Find("input.hills").Input(lean);

    //  ADR 0007, T053b: once the slider stays still after a re-rank, a background search at its
    //  Lean adds Loops leant further, which replace the pager's.
    private protected static readonly TimeSpan Settling = TimeSpan.FromSeconds(3);

    // A search starting renders nothing, so it's polled for, not waited for on renders.
    private protected void UntilSearched() => Assert.That(SpinWait.SpinUntil(() => _planner.Leaning.Searches.Count > 0, Settling), Is.True, "No search started");

    //  R1.1a, STORY-009 AC1b (T056): the last Request's settings are restored when the planner
    //  opens, and kept when Go is pressed or the Hills slider moves.
    //  T060: car parks around here, for the part of the map in view, at once (user, 2026-10-30).
    private protected static readonly FoundStart SheenGate = new("Sheen Gate", new LatLon(51.4562, -0.2641), 338_000);
    private protected static readonly FoundStart Wimbledon = new("Wimbledon Common", new LatLon(51.4373, -0.2366), 126_000);
    private protected static readonly FoundStart Pond = new("Pen Ponds", new LatLon(51.4440, -0.2720), 338_000);
    private protected static readonly MapBounds Richmond = new(51.425, -0.32, 51.465, -0.23);

    private protected static void ViewTheMap(IRenderedComponent<Planner> screen, MapBounds view, int zoom = 13) =>
        screen.InvokeAsync(() => screen.FindComponent<MapView>().Instance.ViewChanged(view.South, view.West, view.North, view.East, zoom)).GetAwaiter().GetResult();

    //  T064: search by name and postcode, to take the map to a new area; the start stays.
    private protected static readonly FoundPlace Dorking = new("Dorking", PlaceKind.Town, "Mole Valley", new LatLon(51.2320, -0.3320));
    private protected static readonly FoundPlace BoxHillPostcode = new("RH5 6BY", PlaceKind.Postcode, "Box Hill", new LatLon(51.2515, -0.3145));

    private protected static void Type(IRenderedComponent<Planner> screen, string text) => screen.Find(".place-search input").Input(text);

    private protected IRenderedComponent<Planner> OpenAfter(RequestSettings last)
    {
        _context.Services.AddSingleton<ILastRequest>(_last = new FakeLastRequest(last));
        return Open(KenwoodHouse);
    }
}
