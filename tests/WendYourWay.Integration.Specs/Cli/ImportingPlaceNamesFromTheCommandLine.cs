using WendYourWay.Cli;
using WendYourWay.Core.Places;
using WendYourWay.Core.Network;
using WendYourWay.Data;
using WendYourWay.Data.Places;
using Npgsql;

namespace WendYourWay.Integration.Specs.Cli;

/// <summary>
/// `wend places import &lt;folder&gt;` (PLAN T064): OS Open Names' places, postcodes and green areas
/// for England and Wales, with the map's own named green areas, searched by name and postcode.
/// Fixtures/open-names is OS Open Names' layout with invented entries round Box Hill and Cardiff;
/// kept-from-osm.osm has "The Park" beside Box Hill.
/// </summary>
[TestFixture]
[NonParallelizable]
public class ImportingPlaceNamesFromTheCommandLine
{
    private static readonly LatLon BoxHill = new(51.2510, -0.3150);

    private (int ExitCode, string Output) _import;
    private NpgsqlDataSource _dataSource = null!;
    private PostgisPlaceSearch _search = null!;

    private static async Task<(int ExitCode, string Output)> Ct(params string[] args)
    {
        using var output = new StringWriter();
        var exitCode = await CommandLine.Run(args, output);
        return (exitCode, output.ToString());
    }

    [OneTimeSetUp]
    public async Task Arrange()
    {
        TestDatabase.Wipe();
        var build = await Ct("network", "build", Fixtures.Path("kept-from-osm.osm"),
            "--database", TestDatabase.ConnectionString, "--rights-of-way", Fixtures.Path("rights-of-way"), "--osm2pgsql", Tools.Osm2pgsql);
        Assert.That(build.ExitCode, Is.Zero, build.Output);
        _import = await Ct("places", "import", Fixtures.Path("open-names"), "--database", TestDatabase.ConnectionString);
        _dataSource = Database.CreateDataSource(TestDatabase.ConnectionString);
        _search = new PostgisPlaceSearch(_dataSource);
    }

    [OneTimeTearDown]
    public void Dispose() => _dataSource?.Dispose();

    private async Task<IReadOnlyList<FoundPlace>> Search(string text) => await _search.SearchAsync(text, BoxHill);

    // Near enough at Box Hill's latitude: a metre of latitude, and of longitude scaled by its cosine.
    private static double Metres(LatLon a, LatLon b) =>
        new NodePoint(a.Longitude * 111_320 * Math.Cos(a.Latitude * Math.PI / 180), a.Latitude * 110_574)
            .DistanceTo(new NodePoint(b.Longitude * 111_320 * Math.Cos(a.Latitude * Math.PI / 180), b.Latitude * 110_574));

    private async Task<string[]> Names(string text) => [.. (await Search(text)).Select(p => p.Name)];

    [Test]
    public void Then_it_succeeds() => Assert.That(_import.ExitCode, Is.Zero, _import.Output);

    [Test]
    public void Then_it_says_what_it_loaded() =>
        Assert.That(_import.Output, Does.Match(@"Places\s+6").And.Match(@"Postcodes\s+3").And.Match(@"Postcode districts\s+2").And.Match(@"Green areas from the map\s+1"));

    [Test]
    public async Task Then_a_place_is_found_from_the_start_of_its_name() =>
        Assert.That((await Names("box"))[0], Is.EqualTo("Box Hill"));

    [Test]
    public async Task Then_a_place_says_what_it_is_and_where() =>
        Assert.That((await Search("dorking"))[0], Is.EqualTo(new FoundPlace("Dorking", PlaceKind.Town, "Mole Valley", (await Search("dorking"))[0].At)));

    [Test]
    public async Task Then_its_position_is_where_OS_puts_it() =>
        Assert.That(Metres((await Search("dorking"))[0].At, new LatLon(51.2320, -0.3320)), Is.LessThan(5));

    [Test]
    public async Task Then_a_later_word_finds_it_too() =>
        Assert.That(await Names("common"), Is.EqualTo(new[] { "Ranmore Common, The" }));

    [Test]
    public async Task Then_a_Welsh_name_finds_the_place() =>
        Assert.That((await Search("caerdydd")).Select(p => (p.Name, p.Kind)), Is.EqualTo(new[] { ("Caerdydd", PlaceKind.City) }));

    [Test]
    public async Task Then_Scotland_isn_t_there() =>
        Assert.That(await Search("inverness"), Is.Empty);

    [Test]
    public async Task Then_roads_aren_t_there() =>
        Assert.That(await Names("box hill r"), Is.Empty);

    [Test]
    public async Task Then_schools_aren_t_there() =>
        Assert.That(await Search("box hill s"), Is.Empty);

    [TestCase("RH5 6BY")]
    [TestCase("rh56by")]
    [TestCase("rh5 6b")]
    public async Task Then_a_postcode_is_found_with_or_without_its_space(string text) =>
        Assert.That((await Search(text))[0], Is.EqualTo(new FoundPlace("RH5 6BY", PlaceKind.Postcode, "Box Hill", (await Search(text))[0].At)));

    [Test]
    public async Task Then_a_postcode_district_comes_first_for_its_own_code() =>
        Assert.That((await Search("rh5")).Select(p => (p.Name, p.Kind)).First(), Is.EqualTo(("RH5", PlaceKind.District)));

    [Test]
    public async Task Then_a_postcode_district_is_in_the_middle_of_its_postcodes() =>
        Assert.That(Metres((await Search("rh5"))[0].At, (await Search("rh5 6by"))[0].At), Is.InRange(100, 200));

    [Test]
    public async Task Then_the_maps_own_named_green_areas_are_found() =>
        Assert.That((await Search("the park")).Select(p => (p.Name, p.Kind)), Is.EqualTo(new[] { ("The Park", PlaceKind.Park) }));

    //  A start clicked on the map: the postcode it's in, near enough, for the search box.
    [Test]
    public async Task Then_the_postcode_nearest_a_place_is_found() =>
        Assert.That((await _search.NearestPostcodeAsync(new LatLon(51.2516, -0.3146)))?.Name, Is.EqualTo("RH5 6BY"));

    [Test]
    public async Task Then_no_postcode_is_found_more_than_a_kilometre_off() =>
        Assert.That(await _search.NearestPostcodeAsync(new LatLon(51.4810, -3.1790)), Is.Null);

    [Test]
    public async Task Then_importing_again_replaces_what_was_there()
    {
        var again = await Ct("places", "import", Fixtures.Path("open-names"), "--database", TestDatabase.ConnectionString);
        Assert.That((again.ExitCode, await Names("box")), Is.EqualTo((0, new[] { "Box Hill" })));
    }

    [Test]
    public async Task Then_a_missing_folder_fails_and_says_so()
    {
        var (exitCode, output) = await Ct("places", "import", "no-such-folder", "--database", TestDatabase.ConnectionString);
        Assert.That((exitCode, output.Contains("no-such-folder", StringComparison.Ordinal)), Is.EqualTo((1, true)));
    }
}
