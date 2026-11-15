using WendYourWay.Data;
using Npgsql;
using WendYourWay.Data.Building;

namespace WendYourWay.Integration.Specs.Data;

/// <summary>
/// A build stopped while PostgreSQL is working (Ctrl+C on `wend`) must stop there too: left
/// running, it holds the Network's rows locked, and the next build waits behind it for up to an
/// hour on Great Britain (review after T030). On Fixtures/richmond-park.osm.gz.
/// </summary>
[TestFixture]
[NonParallelizable]
public class CancellingABuild
{
    private NpgsqlDataSource _dataSource = null!;
    private long _segmentsBefore;
    private Exception? _stopped;

    private async Task<long> Scalar(string sql)
    {
        await using var query = _dataSource.CreateCommand(sql);
        return Convert.ToInt64(await query.ExecuteScalarAsync());
    }

    private const string BuildRunning = """
        select count(*) from pg_stat_activity
         where datname = current_database() and pid <> pg_backend_pid()
           and state = 'active' and query like '%build_segments()%'
        """;

    [OneTimeSetUp]
    public async Task Arrange()
    {
        TestDatabase.Wipe();
        Migrations.Apply(TestDatabase.ConnectionString);
        OsmImport.Run(Tools.Osm2pgsql, TestDatabase.ConnectionString, Fixtures.Path("richmond-park.osm.gz"));
        _dataSource = Database.CreateDataSource(TestDatabase.ConnectionString);
        await NetworkBuild.Build(_dataSource);
        _segmentsBefore = await Scalar("select count(*) from network.segments");

        //  A second build, stopped once PostgreSQL is seen cutting Segments.
        using var stop = new CancellationTokenSource();
        var build = NetworkBuild.Build(_dataSource, cancellationToken: stop.Token);
        while (!build.IsCompleted && await Scalar(BuildRunning) == 0) await Task.Delay(10);
        await stop.CancelAsync();
        try
        {
            await build;
        }
        catch (OperationCanceledException e)
        {
            _stopped = e;
        }
    }

    [OneTimeTearDown]
    public void Dispose() => _dataSource.Dispose();

    [Test]
    public void Then_the_build_stops() => Assert.That(_stopped, Is.Not.Null);

    [Test]
    public async Task Then_nothing_is_left_running_in_the_database() =>
        Assert.That(await Scalar(BuildRunning), Is.Zero);

    [Test]
    public async Task Then_the_first_build_is_still_whole() =>
        Assert.That(await Scalar("select count(*) from network.segments"), Is.EqualTo(_segmentsBefore));
}
