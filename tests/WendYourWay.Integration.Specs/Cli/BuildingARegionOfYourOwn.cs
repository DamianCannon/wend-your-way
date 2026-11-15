using WendYourWay.Cli;
using WendYourWay.Data;

namespace WendYourWay.Integration.Specs.Cli;

/// <summary>
/// A region of your own (#147, T143, STORY-046 AC2; user: "how other people can use my github
/// repository to set this up for themselves"): `wend network build --region surrey` finds the county in
/// Geofabrik's index, downloads its extract and builds the Network from it. England and Wales only.
/// A stand-in for Geofabrik serves an index of a few regions and Fixtures/same-ground.osm as one.
/// </summary>
[TestFixture]
[NonParallelizable]
public class BuildingARegionOfYourOwn
{
    private ExtractServer _server = null!;
    private string _data = null!;

    [SetUp]
    public void Arrange()
    {
        TestDatabase.Wipe();
        _server = new ExtractServer("same-ground", File.ReadAllBytes(Fixtures.Path("same-ground.osm")), new DateTimeOffset(2026, 11, 9, 21, 0, 0, TimeSpan.Zero));
        _server.Index = """
            {"type": "FeatureCollection", "features": [
              {"type": "Feature", "properties": {"id": "europe", "name": "Europe", "urls": {"pbf": "https://example.invalid/europe-latest.osm.pbf"}}},
              {"type": "Feature", "properties": {"id": "united-kingdom", "parent": "europe", "name": "United Kingdom", "urls": {"pbf": "https://example.invalid/uk-latest.osm.pbf"}}},
              {"type": "Feature", "properties": {"id": "england", "parent": "united-kingdom", "name": "England", "urls": {"pbf": "https://example.invalid/england-latest.osm.pbf"}}},
              {"type": "Feature", "properties": {"id": "wales", "parent": "united-kingdom", "name": "Wales", "urls": {"pbf": "https://example.invalid/wales-latest.osm.pbf"}}},
              {"type": "Feature", "properties": {"id": "scotland", "parent": "united-kingdom", "name": "Scotland", "urls": {"pbf": "https://example.invalid/scotland-latest.osm.pbf"}}},
              {"type": "Feature", "properties": {"id": "greater-london", "parent": "england", "name": "Greater London", "urls": {"pbf": "https://example.invalid/greater-london-latest.osm.pbf"}}},
              {"type": "Feature", "properties": {"id": "surrey", "parent": "england", "name": "Surrey", "urls": {"pbf": "SURREY_EXTRACT"}}}
            ]}
            """.Replace("SURREY_EXTRACT", _server.ExtractUrl, StringComparison.Ordinal);
        _data = Directory.CreateTempSubdirectory("wend-region-").FullName;
    }

    [TearDown]
    public void Dispose()
    {
        _server.Dispose();
        Directory.Delete(_data, recursive: true);
    }

    private static async Task<(int ExitCode, string Output)> Wend(params string[] args)
    {
        using var output = new StringWriter();
        var exitCode = await CommandLine.Run(args, output);
        return (exitCode, output.ToString());
    }

    private Task<(int ExitCode, string Output)> Build(string region) => Wend("network", "build", "--region", region, "--index", _server.IndexUrl, "--data", _data,
        "--database", TestDatabase.ConnectionString, "--osm2pgsql", Tools.Osm2pgsql, "--rights-of-way", Fixtures.Path("rights-of-way"));

    private static async Task<long> Segments()
    {
        await using var dataSource = Database.CreateDataSource(TestDatabase.ConnectionString);
        await using var count = dataSource.CreateCommand("select count(*) from network.segments");
        return (long)(await count.ExecuteScalarAsync())!;
    }

    [Test]
    public async Task The_regions_of_England_and_Wales_are_listed()
    {
        var (exitCode, output) = await Wend("network", "regions", "--index", _server.IndexUrl);
        Assert.That((exitCode, output), Is.EqualTo((0, output)));
        Assert.That(output, Does.Contain("surrey").And.Contain("greater-london").And.Contain("wales").And.Contain("england").And.Not.Contain("scotland"));
    }

    [Test]
    public async Task A_region_is_downloaded_and_built()
    {
        var (exitCode, output) = await Build("surrey");
        Assert.That((exitCode, await Segments() > 0, File.Exists(Path.Combine(_data, "same-ground-latest.osm"))), Is.EqualTo((0, true, true)), output);
    }

    //  Named as people write it, or as Geofabrik does.
    [TestCase("Surrey")]
    [TestCase("SURREY")]
    public async Task A_region_is_found_by_its_name_too(string name)
    {
        var (exitCode, output) = await Build(name);
        Assert.That(exitCode, Is.Zero, output);
    }

    [Test]
    public async Task One_outside_England_and_Wales_is_refused()
    {
        var (exitCode, output) = await Build("scotland");
        Assert.That((exitCode, output.Contains("England and Wales only", StringComparison.Ordinal), _server.Asked.Contains("/same-ground-latest.osm")), Is.EqualTo((1, true, false)), output);
    }

    [Test]
    public async Task One_not_known_says_where_to_look()
    {
        var (exitCode, output) = await Build("surey");
        Assert.That((exitCode, output.Trim()), Is.EqualTo((1, "No region called 'surey' in England or Wales. Did you mean surrey? wend network regions lists them all.")));
    }
}
