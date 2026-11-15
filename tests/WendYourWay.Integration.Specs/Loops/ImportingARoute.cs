using WendYourWay.Core.Loops;
using WendYourWay.Core.Scoring;
using WendYourWay.Data;
using WendYourWay.Data.Planning;
using Npgsql;
using WendYourWay.Data.Building;

namespace WendYourWay.Integration.Specs.Loops;

/// <summary>
/// A GPX route imported (T083): matched onto the Network and offered alone, to nudge and save as a
/// Loop offered is. On Fixtures/richmond-park.osm.gz, the GPX being a Loop of Wend Your Way's own as
/// its export carries it, whole, cut short, thinned out or pushed off the ways in the middle.
/// </summary>
[TestFixture]
[NonParallelizable]
public class ImportingARoute
{
    private static readonly LatLon PembrokeLodge = new(51.4436, -0.2963);

    private NpgsqlDataSource _dataSource = null!;
    private LoopPlanner _planner = null!;
    private PlannedLoop _planned = null!;
    private IOfferedLoops _imported = null!;

    [OneTimeSetUp]
    public async Task Arrange()
    {
        TestDatabase.Wipe();
        Migrations.Apply(TestDatabase.ConnectionString);
        OsmImport.Run(Tools.Osm2pgsql, TestDatabase.ConnectionString, Fixtures.Path("richmond-park.osm.gz"));
        _dataSource = Database.CreateDataSource(TestDatabase.ConnectionString);
        await NetworkBuild.Build(_dataSource);
        _planner = new LoopPlanner(new PostgisNetworkSlices(_dataSource), new MapGeometry(_dataSource), ScoringWeights.Default, matching: new RouteMatching(_dataSource));
        _planned = (await _planner.PlanAsync(PembrokeLodge, distanceKm: 10))[0];
        _imported = await _planner.ImportAsync(_planned.Line);
    }

    [OneTimeTearDown]
    public void Dispose() => _dataSource.Dispose();

    private static double Metres(LatLon a, LatLon b)
    {
        var dy = (a.Latitude - b.Latitude) * 111_200;
        var dx = (a.Longitude - b.Longitude) * 111_200 * Math.Cos(a.Latitude * Math.PI / 180);
        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    [Test]
    public void It_is_offered_alone_as_long_as_the_Loop_it_was()
    {
        Assert.That(_imported, Has.Count.EqualTo(1));
        Assert.That(_imported[0].Breakdown.LengthM, Is.EqualTo(_planned.Breakdown.LengthM).Within(1).Percent);
    }

    [Test]
    public void Ending_where_it_began_it_is_closed_back_to_its_start() =>
        Assert.That(Metres(_imported[0].Line[^1], _imported[0].Start), Is.LessThan(1));

    [Test]
    public void It_has_no_gaps() => Assert.That(_imported[0].Gaps, Is.Empty);

    [Test]
    public void It_can_be_saved_by_its_Segments() =>
        Assert.That(_imported.TraversalsOf(_imported[0]), Is.Not.Null.And.Not.Empty);

    //  A slider moved: nothing to re-rank, the same route.
    [Test]
    public async Task Re_ranked_it_is_the_same_route() =>
        Assert.That((await _imported.ReRankAsync(new Leans(1, 1)))[0], Is.SameAs(_imported[0]));

    [Test]
    public async Task It_is_nudged_with_no_length_window()
    {
        var nudged = await _imported.NudgeAsync(_imported[0], new AvoidNudge(_imported[0].Line[_imported[0].Line.Count / 2]), Leans.Centre);
        Assert.That((nudged is not null, nudged?.KmOutsideWindow, nudged?.Loop.Avoids.Count), Is.EqualTo((true, (double?)0, (int?)1)));
    }

    //  Point to point: the first half of the Loop, ending far from where it began, partway along a
    //  way. Matched to the last junction it passes (here 126 m before its end), not back round:
    //  the Breakdown is the match's; the line follows the GPX on to its end (see below).
    [Test]
    public async Task One_ending_elsewhere_is_matched_to_its_last_junction_and_not_closed()
    {
        var half = _planned.Line.Take(_planned.Line.Count / 2).ToArray();
        var gpxM = half.Zip(half.Skip(1), Metres).Sum();
        var route = (await _planner.ImportAsync(half))[0];
        Assert.That((Metres(route.Line[^1], route.Start) > 500, Math.Abs(route.Breakdown.LengthM - gpxM) < 0.05 * gpxM, route.Gaps.Count), Is.EqualTo((true, true, 0)));
    }

    //  T086: a route reaching further than a 100 km Request's slice (46.5 km from its middle) isn't
    //  read: here 100 km west from Pembroke Lodge, point to point, 50 km from its middle.
    [Test]
    public void One_too_spread_out_is_refused_before_any_map_is_read()
    {
        LatLon[] far = [.. Enumerable.Range(0, 101).Select(i => new LatLon(PembrokeLodge.Latitude, PembrokeLodge.Longitude - (i * 0.0144)))];
        Assert.That(() => _planner.ImportAsync(far),
            Throws.InstanceOf<RouteNotMatchedException>().With.Message.Contains("spreads too far"));
    }

    //  A planner's few route points: nothing to follow, and no guess shown.
    [Test]
    public void One_too_sparse_to_follow_is_refused()
    {
        var line = _planned.Line;
        LatLon[] sparse = [line[0], line[line.Count / 3], line[2 * line.Count / 3], line[0]];
        Assert.That(() => _planner.ImportAsync(sparse), Throws.InstanceOf<RouteNotMatchedException>());
    }

    //  Pushed 400 m north for most of its length: under half of it follows the map, and no guess
    //  is shown, though the rest matched.
    [Test]
    public void One_mostly_off_the_map_is_refused()
    {
        var line = _planned.Line;
        var (from, to) = (line.Count / 6, line.Count * 5 / 6);
        LatLon[] pushed = [.. line.Select((p, i) => i > from && i < to ? new LatLon(p.Latitude + 0.0036, p.Longitude) : p)];
        Assert.That(() => _planner.ImportAsync(pushed), Throws.InstanceOf<RouteNotMatchedException>());
    }

    //  Pushed 400 m north for a stretch in the middle: off the ways there, a gap, drawn along the
    //  GPX's own points.
    [Test]
    public async Task A_stretch_off_the_map_is_a_gap_along_its_own_points()
    {
        var line = _planned.Line;
        var (from, to) = (line.Count * 2 / 5, line.Count * 3 / 5);
        var pushed = line.Select((p, i) => i > from && i < to ? new LatLon(p.Latitude + 0.0036, p.Longitude) : p).ToArray();
        var route = (await _planner.ImportAsync(pushed))[0];

        Assert.That(route.Gaps, Is.Not.Empty);
        Assert.That(route.Gaps.SelectMany(g => g.Line).Count(pushed[(from + 1)..to].Contains), Is.GreaterThan(0));
    }

    //  T097: what it keeps for the app's stars, saved: the share of it on the map, its gaps left out.
    [Test]
    public async Task A_route_with_a_gap_is_scored_for_the_part_of_it_on_the_map()
    {
        var line = _planned.Line;
        var (from, to) = (line.Count * 2 / 5, line.Count * 3 / 5);
        var pushed = line.Select((p, i) => i > from && i < to ? new LatLon(p.Latitude + 0.0036, p.Longitude) : p).ToArray();
        var offered = await _planner.ImportAsync(pushed);
        var route = offered[0];
        var gapsM = route.Gaps.Sum(g => g.LengthM);
        Assert.That(offered.ScoreOf(route)!.MatchedShare, Is.EqualTo(route.Breakdown.LengthM / (route.Breakdown.LengthM + gapsM)).Within(1e-9).And.LessThan(1));
    }

    //  Where the match doesn't cover the GPX the route follows the file's own points (user,
    //  2026-11-08): from its first point to its last, open or closed, the start pin there too.
    [Test]
    public void Closed_it_starts_and_ends_where_the_GPX_does()
    {
        var line = _imported[0].Line;
        Assert.That((Metres(line[0], _planned.Line[0]), Metres(line[^1], _planned.Line[^1]), Metres(_imported[0].Start, _planned.Line[0])),
            Is.EqualTo((0.0, 0.0, 0.0)).Within(1));
    }

    //  Starting halfway along a straight stretch of way 60 m or more long, at least 30 m from any
    //  junction: so there's a piece before its first junction to follow.
    //  Closed with no gaps: once round, not twice. Its last junction is its first, near the GPX's
    //  start as well as its end (found 2026-11-08: the line went round again).
    [Test]
    public void Closed_its_line_is_as_long_as_the_GPX()
    {
        double LengthOf(IReadOnlyList<LatLon> l) => l.Zip(l.Skip(1), Metres).Sum();
        Assert.That(LengthOf(_imported[0].Line), Is.EqualTo(LengthOf(_planned.Line)).Within(2).Percent);
    }

    [Test]
    public async Task Point_to_point_it_starts_and_ends_where_the_GPX_does()
    {
        var line = _planned.Line;
        var k = Enumerable.Range(0, line.Count - 1).First(i => Metres(line[i], line[i + 1]) >= 60);
        var middle = new LatLon((line[k].Latitude + line[k + 1].Latitude) / 2, (line[k].Longitude + line[k + 1].Longitude) / 2);
        LatLon[] half = [middle, .. line.Skip(k + 1).Take(line.Count / 2)];
        var route = (await _planner.ImportAsync(half))[0];
        Assert.That((Metres(route.Line[0], half[0]), Metres(route.Line[^1], half[^1]), Metres(route.Start, half[0])), Is.EqualTo((0.0, 0.0, 0.0)).Within(1));
    }

    //  Across a gap, the GPX's own points, not a straight line from where the match stops to where
    //  it starts again (which was 1.8 km across here): the line about as long as the GPX.
    [Test]
    public async Task Across_a_gap_it_follows_the_GPX_s_own_points()
    {
        var line = _planned.Line;
        var (from, to) = (line.Count * 2 / 5, line.Count * 3 / 5);
        LatLon[] pushed = [.. line.Select((p, i) => i > from && i < to ? new LatLon(p.Latitude + 0.0036, p.Longitude) : p)];
        var route = (await _planner.ImportAsync(pushed))[0];
        double LengthOf(IReadOnlyList<LatLon> l) => l.Zip(l.Skip(1), Metres).Sum();
        Assert.That(LengthOf(route.Line), Is.EqualTo(LengthOf(pushed)).Within(5).Percent);
    }

    [Test]
    public async Task Nudged_it_still_follows_the_GPX_across_its_gaps_and_to_its_ends()
    {
        var line = _planned.Line;
        var (from, to) = (line.Count * 2 / 5, line.Count * 3 / 5);
        LatLon[] pushed = [.. line.Skip(3).Select((p, i) => i > from && i < to ? new LatLon(p.Latitude + 0.0036, p.Longitude) : p)];
        var offered = await _planner.ImportAsync(pushed);
        var nudged = (await offered.NudgeAsync(offered[0], new AvoidNudge(offered[0].Line[offered[0].Line.Count / 8]), Leans.Centre))!.Loop;
        var longestStep = nudged.Line.Zip(nudged.Line.Skip(1), Metres).Max();
        Assert.That((Metres(nudged.Line[0], pushed[0]) < 1, Metres(nudged.Line[^1], pushed[^1]) < 1, longestStep < 600), Is.EqualTo((true, true, true)));
    }

    //  Its gaps stay drawn however it's nudged: they're where the GPX left the map.
    [Test]
    public async Task Nudged_its_gaps_go_with_it()
    {
        var line = _planned.Line;
        var (from, to) = (line.Count * 2 / 5, line.Count * 3 / 5);
        var offered = await _planner.ImportAsync([.. line.Select((p, i) => i > from && i < to ? new LatLon(p.Latitude + 0.0036, p.Longitude) : p)]);
        var nudged = await offered.NudgeAsync(offered[0], new AvoidNudge(offered[0].Line[offered[0].Line.Count / 8]), Leans.Centre);
        Assert.That(nudged?.Loop.Gaps, Is.EqualTo(offered[0].Gaps));
    }
}
