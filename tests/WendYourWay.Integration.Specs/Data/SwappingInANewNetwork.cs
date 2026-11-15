using WendYourWay.Data;
using Npgsql;
using WendYourWay.Data.Building;

namespace WendYourWay.Integration.Specs.Data;

/// <summary>
/// STORY-004, PLAN T034: a build writes a new Network beside the one the app reads, then swaps it
/// in and keeps the old one as network_prev. Nothing is deleted in place, so a Great Britain
/// rebuild neither doubles the tables nor needs VACUUM FULL. Fixtures/same-ground.osm is built,
/// then Fixtures/topology.osm on top of it.
/// </summary>
[TestFixture]
[NonParallelizable]
public class SwappingInANewNetwork
{
    private NpgsqlDataSource _dataSource = null!;
    private string _firstNetwork = null!;
    private string _firstStructure = null!;
    private long _firstSeenOnTheOpenConnection;
    private long _secondSeenOnTheOpenConnection;

    //  The Network's contents, by OSM identity and ground: what a build mustn't touch once built.
    private const string Contents = """
        select count(*) || ':' || coalesce(sum(osm_way_id + from_node_id + to_node_id), 0) || ':' ||
               count(*) filter (where ground = 'connector') || ':' || count(*) filter (where main_road)
          from {0}.segments
        """;

    //  Columns, defaults and generated expressions, constraints, indexes and storage settings,
    //  with the schema's name taken out.
    private const string Structure = """
        select string_agg(line, E'\n' order by line) from (
          select 'column ' || c.table_name || '.' || c.column_name || ' ' || c.data_type || ' ' ||
                 coalesce(replace(c.column_default, '{0}.', ''), '') || ' ' || coalesce(c.generation_expression, '') as line
            from information_schema.columns c where c.table_schema = '{0}'
          union all
          select 'constraint ' || con.conname || ' ' || pg_get_constraintdef(con.oid)
            from pg_constraint con join pg_namespace n on n.oid = con.connamespace where n.nspname = '{0}'
          union all
          select 'index ' || replace(indexdef, '{0}.', '') from pg_indexes where schemaname = '{0}'
          union all
          select 'options ' || c.relname || ' ' || array_to_string(c.reloptions, ',')
            from pg_class c join pg_namespace n on n.oid = c.relnamespace where n.nspname = '{0}' and c.reloptions is not null) lines
        """;

    private async Task<string> Text(string sql, string schema)
    {
        await using var query = _dataSource.CreateCommand(string.Format(System.Globalization.CultureInfo.InvariantCulture, sql, schema));
        return Convert.ToString(await query.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture)!;
    }

    [OneTimeSetUp]
    public async Task Arrange()
    {
        TestDatabase.Wipe();
        Migrations.Apply(TestDatabase.ConnectionString);
        _dataSource = Database.CreateDataSource(TestDatabase.ConnectionString);
        OsmImport.Run(Tools.Osm2pgsql, TestDatabase.ConnectionString, Fixtures.Path("same-ground.osm"));
        await NetworkBuild.Build(_dataSource);
        _firstNetwork = await Text(Contents, "network");
        _firstStructure = await Text(Structure, "network");

        //  A connection the app might hold open from before the swap to after it.
        await using var open = await _dataSource.OpenConnectionAsync();
        _firstSeenOnTheOpenConnection = await CountRoad200(open);
        OsmImport.Run(Tools.Osm2pgsql, TestDatabase.ConnectionString, Fixtures.Path("topology.osm"));
        await NetworkBuild.Build(_dataSource);
        _secondSeenOnTheOpenConnection = await CountRoad200(open);
    }

    //  Way 200 is a road in same-ground.osm and isn't in topology.osm.
    private static async Task<long> CountRoad200(NpgsqlConnection connection)
    {
        await using var query = new NpgsqlCommand("select count(*) from network.segments where osm_way_id = 200", connection);
        return (long)(await query.ExecuteScalarAsync())!;
    }

    [OneTimeTearDown]
    public void Dispose() => _dataSource.Dispose();

    [Test]
    public async Task Then_the_previous_Network_is_kept_untouched_as_network_prev() =>
        Assert.That(await Text(Contents, "network_prev"), Is.EqualTo(_firstNetwork));

    [Test]
    public async Task Then_the_new_Networks_Segment_ids_start_afresh() =>
        Assert.That(await Text("select min(id) from {0}.segments", "network"), Is.EqualTo("1"));

    //  Copying a table's structure renames its indexes; a migration naming one would then fail
    //  on the user's database only, after a swap.
    [Test]
    public async Task Then_the_new_Network_is_built_exactly_as_the_migrations_define_it() =>
        Assert.That(await Text(Structure, "network"), Is.EqualTo(_firstStructure));

    [Test]
    public void Then_a_connection_open_across_the_swap_sees_the_new_Network() =>
        Assert.That((_firstSeenOnTheOpenConnection > 0, _secondSeenOnTheOpenConnection), Is.EqualTo((true, 0L)));

    [Test]
    public async Task Then_no_half_built_Network_is_left_beside_it() =>
        Assert.That(await Text("select count(*) from pg_namespace where nspname = '{0}'", "network_next"), Is.EqualTo("0"));

    //  Stored in order of place, so a slice reads neighbouring pages (T055): 40 km from Kenwood
    //  House read 1,068,400 Segments from 213,000 pages scattered over Great Britain's table.
    [Test]
    public async Task Then_the_new_Networks_Segments_are_stored_in_order_of_place() =>
        Assert.That(await Text("select indisclustered from pg_index where indexrelid = '{0}.ix_segments_place'::regclass", "network"), Is.EqualTo("True"));
}
