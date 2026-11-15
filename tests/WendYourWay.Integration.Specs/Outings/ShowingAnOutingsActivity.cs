using WendYourWay.Core.Loops;
using WendYourWay.Data;
using WendYourWay.Data.Outings;
using Npgsql;

namespace WendYourWay.Integration.Specs.Outings;

/// <summary>
/// The Activity an Outing is shown, and made a route, for (T125): on a bike a Gravel Ride's access,
/// on foot a Run's. It's read from the Outing's Activity since T121, where it read its kind: a ride
/// on the road is kind `ride` with no Activity, a walk kind `walk` with a Run's. Made-up Outings on a
/// short line, no Network needed.
/// </summary>
[TestFixture]
[NonParallelizable]
public class ShowingAnOutingsActivity
{
    private NpgsqlDataSource _dataSource = null!;

    [OneTimeSetUp]
    public void Arrange()
    {
        TestDatabase.Wipe();
        Migrations.Apply(TestDatabase.ConnectionString);
        _dataSource = Database.CreateDataSource(TestDatabase.ConnectionString);
    }

    [OneTimeTearDown]
    public void Dispose() => _dataSource.Dispose();

    private async Task<Activity> ShownAs(string kind, string? activity)
    {
        await using var insert = _dataSource.CreateCommand("""
            insert into app.outings (kind, activity, name, source_file, track_hash, geom)
            values (@kind::outing_kind, @activity::activity, @kind, 't125-test', @kind,
                    ST_MakeLine(ST_MakePoint(500000, 180000), ST_MakePoint(500100, 180000))::geometry(LineString, 27700))
            returning id
            """);
        insert.Parameters.Add(new NpgsqlParameter("kind", kind));
        insert.Parameters.Add(new NpgsqlParameter("activity", (object?)activity ?? DBNull.Value));
        var id = (int)(await insert.ExecuteScalarAsync())!;
        return (await new PostgisOutingViews(_dataSource).ViewAsync(id))!.Activity;
    }

    [TestCase("ride", null, Activity.GravelRide)]
    [TestCase("gravel_ride", "gravel_ride", Activity.GravelRide)]
    [TestCase("run", "run", Activity.Run)]
    [TestCase("walk", "run", Activity.Run)]
    [TestCase("hike", "run", Activity.Run)]
    public async Task By_its_kind_it_is_shown_for_an_Activity(string kind, string? activity, Activity shown) =>
        Assert.That(await ShownAs(kind, activity), Is.EqualTo(shown));
}
