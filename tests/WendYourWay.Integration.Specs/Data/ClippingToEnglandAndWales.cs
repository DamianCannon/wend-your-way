using WendYourWay.Data;
using Npgsql;
using WendYourWay.Data.Building;

namespace WendYourWay.Integration.Specs.Data;

/// <summary>
/// STORY-002 AC1 (PLAN T030): the Network covers England and Wales, whose boundaries come from
/// OSM's country relations in the extract. On Fixtures/border.osm.
/// </summary>
[TestFixture]
[NonParallelizable]
public class ClippingToEnglandAndWales
{
    private NpgsqlDataSource _dataSource = null!;
    private SegmentBuild _build = null!;

    [OneTimeSetUp]
    public async Task Arrange()
    {
        TestDatabase.Wipe();
        Migrations.Apply(TestDatabase.ConnectionString);
        OsmImport.Run(Tools.Osm2pgsql, TestDatabase.ConnectionString, Fixtures.Path("border.osm"));
        _dataSource = Database.CreateDataSource(TestDatabase.ConnectionString);
        _build = (await NetworkBuild.Build(_dataSource)).Segments;
    }

    [OneTimeTearDown]
    public void Dispose() => _dataSource.Dispose();

    private async Task<List<long>> WaysInTheNetwork()
    {
        await using var query = _dataSource.CreateCommand("select distinct osm_way_id from network.segments order by 1");
        await using var reader = await query.ExecuteReaderAsync();
        var ways = new List<long>();
        while (await reader.ReadAsync()) ways.Add(reader.GetInt64(0));
        return ways;
    }

    [Test]
    public async Task Then_ways_in_Scotland_are_left_out() =>
        Assert.That(await WaysInTheNetwork(), Does.Not.Contain(101L).And.Not.Contain(105L));

    //  A path across the border is kept, so a Loop near it can still use it; the path in none of
    //  the three countries is left out.
    [Test]
    public async Task Then_ways_in_England_and_Wales_and_across_the_border_are_kept() =>
        Assert.That(await WaysInTheNetwork(), Is.EqualTo(new long[] { 100, 102, 103 }));

    //  Nothing is dropped silently. Anything left out but not in Scotland means the boundary
    //  missed some ground: the build's check on its own clip.
    [Test]
    public void Then_the_build_says_what_it_left_out_and_where() =>
        Assert.That((_build.CountriesFound, _build.InScotland, _build.Elsewhere), Is.EqualTo(("GB-ENG,GB-SCT,GB-WLS", 2L, 1L)));
}

/// <summary>
/// A regional extract (London's, say) holds the country relations without most of their ways, so
/// some or all can't be assembled. Clipping to England alone would drop Wales's ground: unless
/// both are whole, nothing is clipped. On Fixtures/border.osm without its Wales relation.
/// </summary>
[TestFixture]
[NonParallelizable]
public class NotClippingWithoutBothCountries
{
    private NpgsqlDataSource _dataSource = null!;
    private SegmentBuild _build = null!;

    [OneTimeSetUp]
    public async Task Arrange()
    {
        var withoutWales = Path.Combine(Directory.CreateTempSubdirectory("cutthrough-border-").FullName, "border-without-wales.osm");
        var osm = await File.ReadAllTextAsync(Fixtures.Path("border.osm"));
        var wales = osm.IndexOf("<relation id=\"9002\">", StringComparison.Ordinal);
        await File.WriteAllTextAsync(withoutWales, osm[..wales] + osm[(osm.IndexOf("</relation>", wales, StringComparison.Ordinal) + "</relation>".Length)..]);

        TestDatabase.Wipe();
        Migrations.Apply(TestDatabase.ConnectionString);
        OsmImport.Run(Tools.Osm2pgsql, TestDatabase.ConnectionString, withoutWales);
        _dataSource = Database.CreateDataSource(TestDatabase.ConnectionString);
        _build = (await NetworkBuild.Build(_dataSource)).Segments;
    }

    [OneTimeTearDown]
    public void Dispose() => _dataSource.Dispose();

    [Test]
    public void Then_nothing_is_clipped_and_the_build_says_which_countries_it_found() =>
        Assert.That((_build.SegmentsWritten, _build.Clipped, _build.CountriesFound), Is.EqualTo((6L, false, "GB-ENG,GB-SCT")));
}
