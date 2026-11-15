using WendYourWay.Core.Network;
using WendYourWay.Data;
using WendYourWay.Data.Outings;
using Npgsql;
using WendYourWay.Data.Building;

namespace WendYourWay.Integration.Specs.Outings;

/// <summary>
/// What the Outings page shows (PLAN T017, STORY-030 AC2, AC3): each Outing listed, and one at a
/// time drawn as its track over the Segments it covered, with its gaps. On Fixtures/same-ground.osm
/// with the invented Run of Fixtures/pavement-and-park.gpx.
/// </summary>
[TestFixture]
[NonParallelizable]
public class ViewingOutings
{
    private NpgsqlDataSource _dataSource = null!;
    private PostgisOutingViews _outings = null!;

    [OneTimeSetUp]
    public async Task Arrange()
    {
        TestDatabase.Wipe();
        Migrations.Apply(TestDatabase.ConnectionString);
        OsmImport.Run(Tools.Osm2pgsql, TestDatabase.ConnectionString, Fixtures.Path("same-ground.osm"));
        _dataSource = Database.CreateDataSource(TestDatabase.ConnectionString);
        await NetworkBuild.Build(_dataSource);
        await OutingImporter.ImportGpxAsync(_dataSource, Fixtures.Path("pavement-and-park.gpx"));
        _outings = new PostgisOutingViews(_dataSource);
    }

    [OneTimeTearDown]
    public void Dispose() => _dataSource.Dispose();

    private async Task<OutingView> TheRun() =>
        (await _outings.ViewAsync((await _outings.ListAsync()).Single().Id))!;

    [Test]
    public async Task Then_each_Outing_is_listed_with_its_date_length_coverage_and_gaps()
    {
        var outing = (await _outings.ListAsync()).Single();
        Assert.Multiple(() =>
        {
            Assert.That((outing.Name, outing.StartedAt, outing.Gaps), Is.EqualTo(("Pavement and Park", (DateTimeOffset?)new DateTimeOffset(2026, 10, 18, 8, 0, 0, TimeSpan.Zero), 1)));
            Assert.That(outing.LengthM, Is.EqualTo(334).Within(3));
            Assert.That(outing.Coverage, Is.EqualTo(0.48).Within(0.03));
        });
    }

    //  The track starts at the pavement's west end and finishes on the open ground to the north.
    [Test]
    public async Task Then_its_track_runs_from_where_it_started_to_where_it_finished()
    {
        var track = (await TheRun()).Track;
        Assert.Multiple(() =>
        {
            Assert.That(track[0].Latitude, Is.EqualTo(51.55505).Within(1e-5));
            Assert.That(track[0].Longitude, Is.EqualTo(-0.1650).Within(1e-5));
            Assert.That(track[^1].Latitude, Is.EqualTo(51.5565).Within(1e-5));
            Assert.That(track[^1].Longitude, Is.EqualTo(-0.1625).Within(1e-5));
        });
    }

    //  A route made from it is matched on every point it recorded, not the map's thinner line: thinned,
    //  the matcher can take a junction on a road above a path's underpass for the path's (user,
    //  2026-11-09: the Green Man Roundabout).
    [Test]
    public async Task Then_a_route_is_made_from_every_point_it_recorded()
    {
        var outing = await TheRun();
        await using var points = _dataSource.CreateCommand("select ST_NPoints(geom) from app.outings");
        Assert.That((outing.RouteTrack.Count, outing.RouteTrack.Count > outing.Track.Count), Is.EqualTo(((int)(await points.ExecuteScalarAsync())!, true)));
    }

    //  The road and its two pavements, all Connectors, each drawn as its own line.
    [Test]
    public async Task Then_the_Segments_it_covered_come_with_their_ground()
    {
        var covered = (await TheRun()).Covered;
        Assert.That(covered.Select(c => (c.Ground, c.Line.Count >= 2)), Is.EqualTo(Enumerable.Repeat((GroundKind.Connector, true), 3)));
    }

    [Test]
    public async Task Then_its_gaps_come_with_their_lengths()
    {
        var gap = (await TheRun()).Gaps.Single();
        Assert.That((gap.LengthM, gap.Line.Count >= 2), Is.EqualTo((140.0, true)).Using<(double, bool)>((a, b) => Math.Abs(a.Item1 - b.Item1) <= 10 && a.Item2 == b.Item2));
    }

    //  What a route made from it is matched for (T084): a Run's, a Gravel Ride's, and a road
    //  ride's, which has no Activity of its own but was on a bike: a Gravel Ride's.
    [Test]
    public async Task Then_it_comes_with_the_Activity_a_route_from_it_is_matched_for()
    {
        async Task<WendYourWay.Core.Loops.Activity> As(string kind, string activity)
        {
            await using var change = _dataSource.CreateCommand($"update app.outings set kind = '{kind}', activity = {activity}");
            await change.ExecuteNonQueryAsync();
            return (await TheRun()).Activity;
        }
        try
        {
            Assert.That((await As("run", "'run'"), await As("gravel_ride", "'gravel_ride'"), await As("ride", "null")),
                Is.EqualTo((WendYourWay.Core.Loops.Activity.Run, WendYourWay.Core.Loops.Activity.GravelRide, WendYourWay.Core.Loops.Activity.GravelRide)));
        }
        finally
        {
            await using var back = _dataSource.CreateCommand("update app.outings set kind = 'run', activity = 'run'");
            await back.ExecuteNonQueryAsync();
        }
    }

    [Test]
    public async Task Then_an_Outing_that_isn_t_there_is_null() =>
        Assert.That(await _outings.ViewAsync(99_999), Is.Null);
}
