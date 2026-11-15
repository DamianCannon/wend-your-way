using WendYourWay.Data;
using Npgsql;
using WendYourWay.Data.Building;

namespace WendYourWay.Integration.Specs.Data;

/// <summary>
/// A big extract keeps its node positions in a flat file, not in PostgreSQL: with them in a 10 GB
/// table and only 400 MB cached, a Great Britain import crawled for nine hours on a few GB of free
/// memory (T035), against 59 minutes once. The flat file is sized by the highest node ID, so only
/// extracts over 1 GB use it. On Fixtures/same-ground.osm, with the file asked for.
/// </summary>
[TestFixture]
[NonParallelizable]
public class ImportingWithFlatNodes
{
    private string _flatNodes = null!;
    private long _waysWithout;
    private long _waysWith;

    private static async Task<long> Ways()
    {
        await using var dataSource = Database.CreateDataSource(TestDatabase.ConnectionString);
        await using var query = dataSource.CreateCommand("select count(*) from osm.ways");
        return (long)(await query.ExecuteScalarAsync())!;
    }

    [OneTimeSetUp]
    public async Task Arrange()
    {
        TestDatabase.Wipe();
        Migrations.Apply(TestDatabase.ConnectionString);
        OsmImport.Run(Tools.Osm2pgsql, TestDatabase.ConnectionString, Fixtures.Path("same-ground.osm"));
        _waysWithout = await Ways();

        _flatNodes = Path.Combine(Directory.CreateTempSubdirectory("cutthrough-flat-nodes-").FullName, "flat-nodes.bin");
        OsmImport.Run(Tools.Osm2pgsql, TestDatabase.ConnectionString, Fixtures.Path("same-ground.osm"), flatNodesFile: _flatNodes);
        _waysWith = await Ways();
    }

    [Test]
    public void Then_the_same_ways_are_kept() => Assert.That(_waysWith, Is.EqualTo(_waysWithout).And.GreaterThan(0));

    [Test]
    public void Then_the_flat_file_is_deleted_afterwards() => Assert.That(File.Exists(_flatNodes), Is.False);

    [TestCase(130_000_000L, false, Description = "Greater London: its node IDs would size the file at about 100 GB, for nothing")]
    [TestCase(2_200_000_000L, true, Description = "Great Britain")]
    public void Then_only_a_big_extract_uses_one(long bytes, bool flat) =>
        Assert.That(OsmImport.UsesFlatNodes(bytes), Is.EqualTo(flat));
}
