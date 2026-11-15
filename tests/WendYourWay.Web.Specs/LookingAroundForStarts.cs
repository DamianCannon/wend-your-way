using WendYourWay.Core.Loops;
using WendYourWay.Core.Network;
using WendYourWay.Data;
using WendYourWay.Data.Planning;
using WendYourWay.Web.Components;
using WendYourWay.Web.Planning;
using Microsoft.Extensions.Logging.Abstractions;

namespace WendYourWay.Web.Specs;

/// <summary>
/// Car parks around here (T060, T062; out of the session, T124d), with no page: read for the map in
/// view, marked on it, and Loop 1 of the first listed compared.
/// </summary>
public class LookingAroundForStarts
{
    private static readonly FoundStart SheenGate = new("Sheen Gate", new LatLon(51.4562, -0.2641), 338_000);
    private static readonly FoundStart PenPonds = new("Pen Ponds", new LatLon(51.4440, -0.2720), 338_000, new CarParkDetails(Toilets: true));
    private static readonly MapBounds Richmond = new(51.425, -0.32, 51.465, -0.23);

    private FakeCarParks _carParks = null!;
    private FakePreviews _previews = null!;
    private StartsAround _starts = null!;

    [SetUp]
    public void Arrange()
    {
        (_carParks, _previews) = (new FakeCarParks { Answer = new CarParksAround([SheenGate, PenPonds], [SheenGate]) }, new FakePreviews());
        _starts = new StartsAround(_carParks, _previews, NullLogger.Instance);
    }

    [TearDown]
    public void Dispose() => _starts.Dispose();

    private async Task Viewing(int zoom = 13)
    {
        _starts.Viewed(new MapViewport(Richmond, zoom));
        await _starts.ReadAsync(10, Activity.Run);
    }

    [Test]
    public void Before_the_map_says_where_it_is_none_are_listed() => Assert.That(_starts.Listed, Is.Null);

    [Test]
    public async Task The_best_in_view_are_listed()
    {
        await Viewing();
        Assert.That(_starts.Listed, Is.EqualTo(new[] { SheenGate }));
    }

    [Test]
    public async Task They_are_asked_for_the_view_the_Distance_and_the_Activity()
    {
        _starts.Viewed(new MapViewport(Richmond, 13));
        await _starts.ReadAsync(12, Activity.GravelRide);
        Assert.That(_carParks.Asked, Is.EqualTo(new[] { (Richmond, 12.0, Activity.GravelRide) }));
    }

    [Test]
    public async Task Zoomed_out_none_are_read()
    {
        await Viewing(zoom: StartsAround.CarParkZoom - 1);
        Assert.That((_carParks.Asked.Count, _starts.Listed), Is.EqualTo((0, Array.Empty<FoundStart>())));
    }

    //  Every one marked, labelled with its paths and what's known of it, the start's chosen.
    [Test]
    public async Task Every_one_in_view_is_marked_the_start_as_chosen()
    {
        await Viewing();
        var map = new RecordingMap();
        await _starts.MarkAsync(map, PenPonds);
        var (starts, chosen) = ((IReadOnlyList<StartOnMap>, int))map.Last("showStarts")!;
        Assert.That((starts.Select(s => s.Label), chosen),
            Is.EqualTo((new[] { "Sheen Gate · 338 km of paths", "Pen Ponds · 338 km of paths · " + PenPonds.Details!.Says }, 1)));
    }

    [Test]
    public async Task With_none_in_view_the_marks_go()
    {
        _carParks.Answer = new CarParksAround([], []);
        await Viewing();
        var map = new RecordingMap();
        await _starts.MarkAsync(map, null);
        Assert.That(map.Names, Is.EqualTo(new[] { "clearStarts" }));
    }

    //  Loop 1 of each of the first listed, worked out after a moment, and said beside it.
    [Test]
    public async Task Each_place_listed_is_compared_and_says_its_Loop_1()
    {
        _previews.Answers[SheenGate.At] = new LoopPreview(9_710, 62, 143);
        await Viewing();
        _starts.Compare(finding: false, 10, Leans.Centre, Activity.Run);
        Assert.That(SpinWait.SpinUntil(() => _starts.PreviewOf(SheenGate, 10, Leans.Centre, Activity.Run) is "9.7 km · 62% green · 143 m up", TimeSpan.FromSeconds(3)), Is.True);
    }

    [Test]
    public async Task A_place_with_no_Loop_says_so()
    {
        await Viewing();
        _starts.Compare(finding: false, 10, Leans.Centre, Activity.Run);
        Assert.That(SpinWait.SpinUntil(() => _starts.PreviewOf(SheenGate, 10, Leans.Centre, Activity.Run) is "No Loop", TimeSpan.FromSeconds(3)), Is.True);
    }

    [Test]
    public async Task While_a_Request_is_found_nothing_is_compared()
    {
        await Viewing();
        _starts.Compare(finding: true, 10, Leans.Centre, Activity.Run);
        Thread.Sleep(800);
        Assert.That(_previews.Asked, Is.Empty);
    }

    //  Kept by place, Distance, Leans and Activity: another Distance hasn't been compared.
    [Test]
    public async Task Compared_for_one_Distance_it_says_nothing_for_another()
    {
        await Viewing();
        _starts.Compare(finding: false, 10, Leans.Centre, Activity.Run);
        SpinWait.SpinUntil(() => _starts.PreviewOf(SheenGate, 10, Leans.Centre, Activity.Run) is not null, TimeSpan.FromSeconds(3));
        Assert.That(_starts.PreviewOf(SheenGate, 12, Leans.Centre, Activity.Run), Is.Null);
    }
}
