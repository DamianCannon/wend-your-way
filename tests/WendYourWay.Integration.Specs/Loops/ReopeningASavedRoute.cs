using WendYourWay.Core.Loops;
using WendYourWay.Core.Network;
using WendYourWay.Core.Scoring;
using WendYourWay.Data;
using WendYourWay.Data.Planning;
using Npgsql;
using WendYourWay.Data.Building;

namespace WendYourWay.Integration.Specs.Loops;

/// <summary>
/// A Saved Route reopened (T085): its slice read again, its Segments found by OSM identity, so it
/// nudges as an offered Loop does; not if the map has changed under it, or it spreads too far
/// (T086). On Fixtures/richmond-park.osm.gz, the routes saved being Wend Your Way's own and an
/// imported one, as their Saved Routes keep them.
/// </summary>
[TestFixture]
[NonParallelizable]
public class ReopeningASavedRoute
{
    private static readonly LatLon PembrokeLodge = new(51.4436, -0.2963);

    private NpgsqlDataSource _dataSource = null!;
    private LoopPlanner _planner = null!;
    private IOfferedLoops _offered = null!;
    private PlannedLoop _planned = null!;

    [OneTimeSetUp]
    public async Task Arrange()
    {
        TestDatabase.Wipe();
        Migrations.Apply(TestDatabase.ConnectionString);
        OsmImport.Run(Tools.Osm2pgsql, TestDatabase.ConnectionString, Fixtures.Path("richmond-park.osm.gz"));
        _dataSource = Database.CreateDataSource(TestDatabase.ConnectionString);
        await NetworkBuild.Build(_dataSource);
        _planner = new LoopPlanner(new PostgisNetworkSlices(_dataSource), new MapGeometry(_dataSource), ScoringWeights.Default, matching: new RouteMatching(_dataSource));
        _offered = await _planner.PlanAsync(PembrokeLodge, distanceKm: 10);
        _planned = _offered[0];
    }

    [OneTimeTearDown]
    public void Dispose() => _dataSource.Dispose();

    private static double Metres(LatLon a, LatLon b)
    {
        var dy = (a.Latitude - b.Latitude) * 111_200;
        var dx = (a.Longitude - b.Longitude) * 111_200 * Math.Cos(a.Latitude * Math.PI / 180);
        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    // As a Saved Route keeps it: what the map drew, and its Segments by OSM identity.
    private Task<IOfferedLoops> Reopen(IOfferedLoops from, PlannedLoop planned) => _planner.ReopenAsync(planned, from.TraversalsOf(planned)!);

    [Test]
    public async Task Reopened_it_is_the_route_saved_with_its_own_Segments()
    {
        var reopened = await Reopen(_offered, _planned);
        Assert.That((reopened.Count, ReferenceEquals(reopened[0], _planned), reopened.TraversalsOf(reopened[0])), Is.EqualTo((1, true, _offered.TraversalsOf(_planned))));
    }

    //  T157 (#161): saved before there were notes, it's reopened with them, worked out from its Segments.
    [Test]
    public async Task Saved_with_no_notes_it_is_reopened_with_them()
    {
        Assume.That(_planned.Notes, Is.Not.Empty, "a Loop with notes");
        var reopened = await _planner.ReopenAsync(_planned with { Notes = [] }, _offered.TraversalsOf(_planned)!);
        Assert.That(reopened[0].Notes, Is.EqualTo(_planned.Notes));
    }

    [Test]
    public async Task Reopened_it_is_nudged_with_no_length_window()
    {
        var reopened = await Reopen(_offered, _planned);
        var nudged = await reopened.NudgeAsync(reopened[0], new AvoidNudge(_planned.Line[_planned.Line.Count / 2]), Leans.Centre);
        Assert.That((nudged is not null, nudged?.KmOutsideWindow, nudged?.Loop.Avoids.Count), Is.EqualTo((true, (double?)0, (int?)1)));
    }

    //  Its stops are its stops again: one can be taken away.
    [Test]
    public async Task Its_stops_are_kept()
    {
        Assume.That(_planned.Anchors, Is.Not.Empty, "a Loop with stops");
        var reopened = await Reopen(_offered, _planned);
        Assert.That(await reopened.NudgeAsync(reopened[0], new RemoveStopNudge(0), Leans.Centre), Is.Not.Null);
    }

    //  Saved after an Avoid: reopened, the Avoid is there to take away again.
    [Test]
    public async Task Its_Avoids_are_kept()
    {
        var avoided = (await _offered.NudgeAsync(_planned, new AvoidNudge(_planned.Line[_planned.Line.Count / 2]), Leans.Centre))!.Loop;
        var reopened = await Reopen(_offered, avoided);
        Assert.That(await reopened.NudgeAsync(reopened[0], new RemoveAvoidNudge(0), Leans.Centre), Is.Not.Null);
    }

    //  An imported route, point to point from partway along a way: nudged after reopening, its
    //  line still follows what was saved to its ends.
    [Test]
    public async Task An_imported_route_reopened_and_nudged_still_follows_what_was_saved()
    {
        var line = _planned.Line;
        var k = Enumerable.Range(0, line.Count - 1).First(i => Metres(line[i], line[i + 1]) >= 60);
        var middle = new LatLon((line[k].Latitude + line[k + 1].Latitude) / 2, (line[k].Longitude + line[k + 1].Longitude) / 2);
        var imported = await _planner.ImportAsync([middle, .. line.Skip(k + 1).Take(line.Count / 2)]);
        var reopened = await Reopen(imported, imported[0]);
        var nudged = (await reopened.NudgeAsync(reopened[0], new AvoidNudge(imported[0].Line[imported[0].Line.Count / 2]), Leans.Centre))!.Loop;
        Assert.That((Metres(nudged.Line[0], middle) < 1, Metres(nudged.Line[^1], imported[0].Line[^1]) < 1), Is.EqualTo((true, true)));
    }

    //  Rebuilt since, one of its ways gone: opened as it was, but not nudged.
    [Test]
    public void One_the_map_has_changed_under_is_refused_saying_so()
    {
        var traversals = _offered.TraversalsOf(_planned)!.Select((t, i) => i == 3 ? (new SegmentKey(999_999_999, 1, 2), t.Forward) : t).ToArray();
        Assert.That(() => _planner.ReopenAsync(_planned, traversals),
            Throws.InstanceOf<RouteNotMatchedException>().With.Message.EqualTo(LoopPlanner.MapChanged));
    }

    //  T086: one spreading further than a 100 km Request reads isn't read either.
    [Test]
    public void One_too_spread_out_is_refused()
    {
        var far = _planned with { Line = [.. _planned.Line, new LatLon(PembrokeLodge.Latitude, PembrokeLodge.Longitude - 1.45)] };
        Assert.That(() => _planner.ReopenAsync(far, _offered.TraversalsOf(_planned)!),
            Throws.InstanceOf<RouteNotMatchedException>().With.Message.EqualTo(RouteMatching.TooSpread));
    }
}
