using System.IO.Compression;
using WendYourWay.Cli;
using WendYourWay.Data;
using WendYourWay.Data.Places;

namespace WendYourWay.Integration.Specs.Cli;

/// <summary>
/// `wend update` brings OS's open data up to date with the map (T171, #175): OS Terrain 50's heights before
/// the build climbs on them, OS Open Names' place names after it, each fetched from OS's Downloads API only
/// when OS has a release newer than the one in use. A stand-in for Geofabrik serving Fixtures/same-ground.osm,
/// and for OS offering Fixtures/open-names and Fixtures/terrain zipped as OS's are.
/// </summary>
[TestFixture]
[NonParallelizable]
public class UpdatingOsDataWithTheMap
{
    private static readonly DateTimeOffset Monday = new(2026, 11, 9, 21, 22, 17, TimeSpan.Zero);

    private ExtractServer _server = null!;
    private string _data = null!;
    private string _terrain = null!;
    private byte[] _names = null!;
    private byte[] _heights = null!;

    [SetUp]
    public async Task Arrange()
    {
        TestDatabase.Wipe();
        _server = new ExtractServer("same-ground", File.ReadAllBytes(Fixtures.Path("same-ground.osm")), Monday);
        _data = Directory.CreateTempSubdirectory("wend-os-update-").FullName;
        // Heights unpacked by hand before T171: the fixture's tile, saying no release.
        _terrain = Path.Combine(_data, "terrain50", "asc");
        Directory.CreateDirectory(_terrain);
        foreach (var tile in Directory.GetFiles(Fixtures.Path("terrain"), "*.asc")) File.Copy(tile, Path.Combine(_terrain, Path.GetFileName(tile)));
        _names = Zipped(Fixtures.Path("open-names"));
        _heights = await HeightsZipAsync();
    }

    [TearDown]
    public void Dispose()
    {
        _server.Dispose();
        Directory.Delete(_data, recursive: true);
    }

    private static byte[] Zipped(string folder)
    {
        using var zip = new MemoryStream();
        ZipFile.CreateFromDirectory(folder, zip);
        return zip.ToArray();
    }

    // OS Terrain 50's zip: a zip for each square, holding its tile.
    private static async Task<byte[]> HeightsZipAsync()
    {
        using var zip = new MemoryStream();
        await using (var outer = new ZipArchive(zip, ZipArchiveMode.Create, leaveOpen: true))
        {
            await using var square = outer.CreateEntry("data/tq/tq15_OST50GRID_20260529.zip").Open();
            using var inner = new ZipArchive(square, ZipArchiveMode.Create);
            inner.CreateEntryFromFile(Directory.GetFiles(Fixtures.Path("terrain"), "*.asc").Single(), "TQ15.asc");
        }
        return zip.ToArray();
    }

    private void OffersNames(string version = "2026-10") => _server.OsProducts["OpenNames"] = (version, "CSV", "opname_csv_gb.zip", _names);

    private void OffersHeights(string version = "2026-07") => _server.OsProducts["Terrain50"] = (version, "ASCII Grid and GML (Grid)", "terr50_gagg_gb.zip", _heights);

    private async Task<(int ExitCode, string Output)> Update()
    {
        using var output = new StringWriter();
        var exitCode = await CommandLine.Run(["update", "--source", _server.ExtractUrl, "--os", _server.OsApi, "--data", _data, "--terrain", _terrain,
            "--database", TestDatabase.ConnectionString, "--osm2pgsql", Tools.Osm2pgsql, "--rights-of-way", Fixtures.Path("rights-of-way")], output);
        return (exitCode, output.ToString());
    }

    private static async Task<PlacesRelease?> NamesInUse()
    {
        await using var dataSource = Database.CreateDataSource(TestDatabase.ConnectionString);
        return (await PlacesImport.ReleaseAsync(dataSource))?.Release;
    }

    [Test]
    public async Task OSs_newer_place_names_are_loaded_and_their_release_kept()
    {
        OffersNames();
        var (exitCode, output) = await Update();
        Assert.That((exitCode, (await NamesInUse())?.Version), Is.EqualTo((0, "2026-10")), output);
    }

    [Test]
    public async Task Loading_them_says_so()
    {
        OffersNames();
        var (_, output) = await Update();
        Assert.That(output, Does.Contain("  place names: OS Open Names 2026-10 loaded, 6 places"));
    }

    [Test]
    public async Task Place_names_already_in_use_arent_loaded_again()
    {
        OffersNames();
        await Update();
        (_server.DataAt, _server.Extract) = (Monday.AddDays(1), [.. _server.Extract, .. "\n"u8.ToArray()]);
        var (_, output) = await Update();
        Assert.That(output, Does.Contain("  place names: OS Open Names 2026-10, OS's newest").And.Not.Contain("2026-10 loaded"));
    }

    [Test]
    public async Task OSs_newer_heights_are_unpacked_and_their_release_said_beside_them()
    {
        OffersHeights();
        var (exitCode, output) = await Update();
        Assert.That((exitCode, TerrainRelease.In(_terrain)?.Version, File.Exists(Path.Combine(_terrain, "TQ15.asc"))), Is.EqualTo((0, "2026-07", true)), output);
    }

    [Test]
    public async Task The_heights_unpacked_are_the_ones_the_build_climbed_on()
    {
        OffersHeights();
        var (_, output) = await Update();
        Assert.That(output, Does.Contain("  heights: OS Terrain 50 2026-07, 1 tile"));
    }

    //  T171: Terrain 50's zip already kept beside its tiles, as unzipped by hand: it's the release in use, said.
    [Test]
    public async Task Heights_whose_zip_is_kept_already_are_said_to_be_its_release_without_fetching_it()
    {
        OffersHeights();
        await File.WriteAllBytesAsync(Path.Combine(_data, "terrain50", "terr50_gagg_gb.zip"), _heights);
        var (_, output) = await Update();
        Assert.That((TerrainRelease.In(_terrain)?.Version, _server.Asked.Contains("/os/files/Terrain50")), Is.EqualTo(("2026-07", false)), output);
    }

    //  OS out of reach: the map's update goes on, its place names and heights as they were, and it says so.
    [Test]
    public async Task Without_OS_the_map_is_still_updated_and_it_says_what_wasnt()
    {
        var (exitCode, output) = await Update();
        Assert.That((exitCode, TerrainRelease.In(_terrain), await NamesInUse()), Is.EqualTo((0, (TerrainRelease?)null, (PlacesRelease?)null)), output);
        Assert.That(output, Does.Contain("  heights not updated:").And.Contain("  place names not updated:"));
    }
}
