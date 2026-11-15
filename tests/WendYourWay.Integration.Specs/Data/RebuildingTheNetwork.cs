using WendYourWay.Data;
using Npgsql;
using WendYourWay.Data.Building;

namespace WendYourWay.Integration.Specs.Data;

/// <summary>
/// A Network build happens all at once or not at all, so a build that stops part-way leaves the
/// Network the app was already using (Slice 1 review). On Fixtures/same-ground.osm.
/// </summary>
[TestFixture]
[NonParallelizable]
public class RebuildingTheNetwork
{
    private NpgsqlDataSource _dataSource = null!;
    private Exception? _interrupted;

    [OneTimeSetUp]
    public async Task Arrange()
    {
        TestDatabase.Wipe();
        Migrations.Apply(TestDatabase.ConnectionString);
        OsmImport.Run(Tools.Osm2pgsql, TestDatabase.ConnectionString, Fixtures.Path("same-ground.osm"));
        _dataSource = Database.CreateDataSource(TestDatabase.ConnectionString);
        await NetworkBuild.Build(_dataSource);

        //  A second build, stopped as soon as its Segments are cut: before it has found which
        //  paths run beside roads.
        using var stop = new CancellationTokenSource();
        try
        {
            await NetworkBuild.Build(_dataSource, phaseDone: (phase, _) => { if (phase == "segments") stop.Cancel(); }, cancellationToken: stop.Token);
        }
        catch (OperationCanceledException e)
        {
            _interrupted = e;
        }
    }

    [OneTimeTearDown]
    public void Dispose() => _dataSource.Dispose();

    [Test]
    public void Then_the_second_build_stops() => Assert.That(_interrupted, Is.Not.Null);

    //  Pavement 210 runs beside road 200: after the first build it's a Connector on the road's
    //  ground. Had the second build's Segments been kept without its same ground, it would be
    //  Traffic-free and beside nothing.
    [Test]
    public async Task Then_the_first_build_is_still_whole()
    {
        await using var query = _dataSource.CreateCommand("""
            select s.ground::text, (select count(*) from network.segment_beside b where b.path_id = s.id)
              from network.segments s where s.osm_way_id = 210
            """);
        await using var reader = await query.ExecuteReaderAsync();
        await reader.ReadAsync();
        Assert.That((reader.GetString(0), reader.GetInt64(1)), Is.EqualTo(("connector", 1L)));
    }
}
