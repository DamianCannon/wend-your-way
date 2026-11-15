using WendYourWay.Data;
using NetTopologySuite.Geometries;
using Npgsql;

namespace WendYourWay.Integration.Specs.Data;

public static class MigratingTheDatabase
{
    [TestFixture]
    [NonParallelizable]
    public class An_empty_database
    {
        private IReadOnlyList<string> _applied = [];
        private double _lengthOfStoredSegment;

        [OneTimeSetUp]
        public async Task Arrange()
        {
            TestDatabase.Wipe();
            _applied = Migrations.Apply(TestDatabase.ConnectionString);

            await using var dataSource = Database.CreateDataSource(TestDatabase.ConnectionString);
            await using var insert = dataSource.CreateCommand("""
                insert into network.segments (osm_way_id, from_node_id, to_node_id, geom, ground, access_run)
                values (1, 10, 20, @geom, 'traffic_free', 'allowed')
                returning length_m
                """);
            var diagonal = new LineString([new Coordinate(0, 0), new Coordinate(300, 400)]) { SRID = 27700 };
            insert.Parameters.Add(new NpgsqlParameter("geom", diagonal));
            _lengthOfStoredSegment = (double)(await insert.ExecuteScalarAsync())!;
        }

        [Test]
        public void Then_the_first_migration_is_applied() =>
            Assert.That(_applied, Has.Some.Contains("0001"));

        [Test]
        public void Then_a_Segment_s_length_comes_from_its_geometry() =>
            Assert.That(_lengthOfStoredSegment, Is.EqualTo(500).Within(1e-9));
    }

    [TestFixture]
    [NonParallelizable]
    public class A_database_that_is_already_up_to_date
    {
        private IReadOnlyList<string> _secondRun = ["not run"];

        [OneTimeSetUp]
        public void Arrange()
        {
            TestDatabase.Wipe();
            Migrations.Apply(TestDatabase.ConnectionString);
            _secondRun = Migrations.Apply(TestDatabase.ConnectionString);
        }

        [Test]
        public void Then_migrating_again_applies_nothing() => Assert.That(_secondRun, Is.Empty);
    }

    [TestFixture]
    [NonParallelizable]
    public class Storing_Segments
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

        private async Task Insert(long wayId, long fromNode, long toNode, int srid)
        {
            await using var insert = _dataSource.CreateCommand("""
                insert into network.segments (osm_way_id, from_node_id, to_node_id, geom, ground, access_run)
                values (@way, @from, @to, @geom, 'off_road', 'allowed')
                """);
            insert.Parameters.Add(new NpgsqlParameter("way", wayId));
            insert.Parameters.Add(new NpgsqlParameter("from", fromNode));
            insert.Parameters.Add(new NpgsqlParameter("to", toNode));
            insert.Parameters.Add(new NpgsqlParameter("geom",
                new LineString([new Coordinate(0, 0), new Coordinate(100, 0)]) { SRID = srid }));
            await insert.ExecuteNonQueryAsync();
        }

        //  User data (Marks, Outings, Saved Loops) refers to Segments by OSM identity (ADR 0005),
        //  so two Segments must never share one.
        [Test]
        public async Task Then_a_second_Segment_with_the_same_OSM_identity_is_refused()
        {
            await Insert(wayId: 7, fromNode: 70, toNode: 71, srid: 27700);
            var duplicate = await Assert.ThrowsAsync<PostgresException>(() => Insert(wayId: 7, fromNode: 70, toNode: 71, srid: 27700));
            Assert.That(duplicate!.SqlState, Is.EqualTo(PostgresErrorCodes.UniqueViolation));
        }

        //  Every length and buffer assumes metres in British National Grid.
        [Test]
        public async Task Then_geometry_in_latitude_and_longitude_is_refused()
        {
            var wrongGrid = await Assert.ThrowsAsync<PostgresException>(() => Insert(wayId: 8, fromNode: 80, toNode: 81, srid: 4326));
            Assert.That(wrongGrid!.MessageText, Does.Contain("SRID"));
        }
    }
}
